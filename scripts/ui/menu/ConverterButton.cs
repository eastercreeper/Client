using Godot;
using System;
using System.Threading.Tasks;

public partial class ConverterButton : Button
{
    private const int SeveritySuccess = 0;
    private const int SeverityWarning = 1;
    private const int SeverityError = 2;

    public override void _Pressed()
    {
        _ = RunConversionAsync();
    }

    private async Task RunConversionAsync()
    {
        try
        {
            (int converted, int failed) = await SSPMToPhxmConverter.BatchConvertAsync(MapUtil.MapsFolder);

            if (converted == 0 && failed == 0)
            {
                _ = ToastNotification.Notify("Converter finished: no SSPM files found.", SeverityWarning);
                return;
            }

            _ = ToastNotification.Notify($"Converter finished: {converted} converted, {failed} failed", failed > 0 ? SeverityWarning : SeveritySuccess);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            _ = ToastNotification.Notify($"Converter failed: {ex.Message}", SeverityError);
        }
    }
}
