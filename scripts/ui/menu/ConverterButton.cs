using Godot;
using System;

public partial class ConverterButton : Button
{
    private const int SeveritySuccess = 0;
    private const int SeverityWarning = 1;
    private const int SeverityError = 2;

    public override void _Pressed()
    {
        RunConversionAsync();
    }

    private async void RunConversionAsync()
    {
        try
        {
            (int converted, int failed) = await SSPMToPhxmConverter.BatchConvertAsync(MapUtil.MapsFolder);

            if (converted == 0 && failed == 0)
            {
                await ToastNotification.Notify("Converter finished: no SSPM files found.", SeverityWarning);
                return;
            }

            await ToastNotification.Notify($"Converter finished: {converted} converted, {failed} failed", failed > 0 ? SeverityWarning : SeveritySuccess);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            await ToastNotification.Notify($"Converter failed: {ex.Message}", SeverityError);
        }
    }
}
