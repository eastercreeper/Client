using Godot;
using System;
using System.Threading.Tasks;

public partial class ConverterButton : Button
{
    public override async void _Pressed()
    {
        try
        {
            (int converted, int failed) = await SSPMToPhxmConverter.BatchConvertAsync(MapUtil.MapsFolder);
            _ = ToastNotification.Notify($"Converter finished: {converted} converted, {failed} failed", failed > 0 ? 1 : 0);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            _ = ToastNotification.Notify($"Converter failed: {ex.Message}", 2);
        }
    }
}
