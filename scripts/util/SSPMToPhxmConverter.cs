using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Utility class for converting SSPM beatmap files to the native PHXM format.
/// Reuses <see cref="MapParser.SSPM(byte[])"/> for parsing and mirrors the
/// encoding logic of <see cref="MapParser.Encode"/> to write output to a
/// caller-specified path instead of the default user maps folder.
/// </summary>
public static class SSPMToPhxmConverter
{
    /// <summary>
    /// Converts a single SSPM file to PHXM format.
    /// </summary>
    /// <param name="sspmPath">
    /// Path to the source SSPM file.
    /// Supports Godot virtual paths (<c>user://</c>, <c>res://</c>) as well as
    /// regular filesystem paths.
    /// </param>
    /// <param name="outputPath">
    /// Destination path for the produced PHXM file.
    /// When <c>null</c>, the output is placed next to the source file with a
    /// <c>.phxm</c> extension.
    /// </param>
    public static void ConvertSSPM(string sspmPath, string outputPath = null)
    {
        string resolvedInput = ResolvePath(sspmPath);

        if (!File.Exists(resolvedInput))
            throw new FileNotFoundException($"SSPM file not found: {resolvedInput}");

        byte[] bytes = File.ReadAllBytes(resolvedInput);
        Map map = MapParser.SSPM(bytes);

        if (outputPath == null)
        {
            string dir = Path.GetDirectoryName(resolvedInput);
            string stem = Path.GetFileNameWithoutExtension(resolvedInput);
            outputPath = Path.Combine(dir ?? string.Empty, $"{stem}.phxm");
        }

        string resolvedOutput = ResolvePath(outputPath);
        string outputDir = Path.GetDirectoryName(resolvedOutput);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        EncodeToPath(map, resolvedOutput);
        Logger.Log($"[SSPMConverter] {Path.GetFileName(resolvedInput)} → {Path.GetFileName(resolvedOutput)}");
    }

    /// <summary>
    /// Batch converts all SSPM files found in <paramref name="inputDir"/> to
    /// PHXM format using parallel processing.
    /// </summary>
    /// <param name="inputDir">
    /// Source directory to scan for <c>*.sspm</c> files (non-recursive).
    /// Supports Godot virtual paths.
    /// </param>
    /// <param name="outputDir">
    /// Destination directory for the produced PHXM files.
    /// Defaults to a <c>phxm_output</c> subdirectory inside <paramref name="inputDir"/>.
    /// </param>
    /// <param name="maxParallelism">
    /// Maximum number of files to convert concurrently.
    /// A value &lt;= 0 uses half of the available logical processors.
    /// </param>
    /// <returns>A tuple of (converted, failed) counts.</returns>
    public static async Task<(int Converted, int Failed)> BatchConvertAsync(
        string inputDir,
        string outputDir = null,
        int maxParallelism = -1)
    {
        string resolvedInput = ResolvePath(inputDir);

        if (!Directory.Exists(resolvedInput))
            throw new DirectoryNotFoundException($"Input directory not found: {resolvedInput}");

        string resolvedOutput = outputDir != null
            ? ResolvePath(outputDir)
            : Path.Combine(resolvedInput, "phxm_output");

        Directory.CreateDirectory(resolvedOutput);

        string[] sspmFiles = Directory.GetFiles(resolvedInput, "*.sspm", SearchOption.TopDirectoryOnly);

        if (sspmFiles.Length == 0)
        {
            Logger.Log($"[SSPMConverter] No SSPM files found in: {resolvedInput}");
            return (0, 0);
        }

        Logger.Log($"[SSPMConverter] Starting batch conversion of {sspmFiles.Length} file(s)...");

        if (maxParallelism <= 0)
            maxParallelism = Math.Max(1, System.Environment.ProcessorCount / 2);

        int converted = 0;
        int failed = 0;

        await Task.Run(() =>
        {
            System.Threading.Tasks.Parallel.ForEach(
                sspmFiles,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = maxParallelism },
                sspmFile =>
                {
                    string fileName = Path.GetFileNameWithoutExtension(sspmFile);
                    string destPath = Path.Combine(resolvedOutput, $"{fileName}.phxm");

                    try
                    {
                        byte[] bytes = File.ReadAllBytes(sspmFile);
                        Map map = MapParser.SSPM(bytes);
                        EncodeToPath(map, destPath);
                        System.Threading.Interlocked.Increment(ref converted);
                        Logger.Log($"[SSPMConverter] ✓ {Path.GetFileName(sspmFile)}");
                    }
                    catch (Exception ex)
                    {
                        System.Threading.Interlocked.Increment(ref failed);
                        Logger.Log($"[SSPMConverter] ✗ {Path.GetFileName(sspmFile)}: {ex.Message}");
                    }
                });
        });

        Logger.Log($"[SSPMConverter] Done — {converted} converted, {failed} failed → {resolvedOutput}");
        return (converted, failed);
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Encodes a <see cref="Map"/> to a PHXM ZIP archive at the given filesystem
    /// path. Mirrors <see cref="MapParser.Encode"/> but writes to an arbitrary
    /// output path and guards against NaN / Infinity in quantum note coordinates.
    /// </summary>
    private static void EncodeToPath(Map map, string outputPath)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create))
        {
            // metadata.json
            var metaEntry = archive.CreateEntry("metadata.json", CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(metaEntry.Open()))
                writer.Write(map.EncodeMeta());

            // objects.phxmo
            var objEntry = archive.CreateEntry("objects.phxmo", CompressionLevel.NoCompression);
            using (var objStream = objEntry.Open())
            using (var bw = new BinaryWriter(objStream))
            {
                bw.Write((uint)12);                 // type count
                bw.Write((uint)map.Notes.Length);   // note count

                foreach (var note in map.Notes)
                {
                    // Guard against NaN / Infinity that can appear in quantum
                    // float coordinates of corrupt SSPM files.
                    float x = float.IsFinite(note.X) ? note.X : 0f;
                    float y = float.IsFinite(note.Y) ? note.Y : 0f;

                    bool quantum = (int)x != x || (int)y != y
                                   || x < -1 || x > 1 || y < -1 || y > 1;
                    bw.Write((uint)note.Millisecond);
                    bw.Write(Convert.ToByte(quantum));
                    if (quantum)
                    {
                        bw.Write(x);
                        bw.Write(y);
                    }
                    else
                    {
                        bw.Write((byte)(x + 1));
                        bw.Write((byte)(y + 1));
                    }
                }

                bw.Write(0); // timing point count
                bw.Write(0); // brightness count
                bw.Write(0); // contrast count
                bw.Write(0); // saturation count
                bw.Write(0); // blur count
                bw.Write(0); // fov count
                bw.Write(0); // tint count
                bw.Write(0); // position count
                bw.Write(0); // rotation count
                bw.Write(0); // ar factor count
                bw.Write(0); // text count
            }

            void AddAsset(string name, byte[] buffer)
            {
                var asset = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using var stream = asset.Open();
                stream.Write(buffer, 0, buffer.Length);
            }

            if (map.AudioBuffer is { Length: > 0 }) AddAsset($"audio.{map.AudioExt}", map.AudioBuffer);
            if (map.CoverBuffer is { Length: > 0 }) AddAsset("cover.png", map.CoverBuffer);
            if (map.VideoBuffer is { Length: > 0 }) AddAsset("video.mp4", map.VideoBuffer);
        }

        File.WriteAllBytes(outputPath, ms.ToArray());
    }

    /// <summary>
    /// Resolves a Godot virtual path (<c>user://</c>, <c>res://</c>) to an
    /// absolute filesystem path; returns the input unchanged otherwise.
    /// </summary>
    private static string ResolvePath(string path)
    {
        if (path.StartsWith("user://") || path.StartsWith("res://"))
            return ProjectSettings.GlobalizePath(path);
        return path;
    }
}
