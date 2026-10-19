using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;

namespace TravelAgency.App.Services;

public static class AppHaptics
{
    public static void Confirm() => Perform(HapticFeedbackType.Click);

    public static void Reject() => Perform(HapticFeedbackType.LongPress);

    private static void Perform(HapticFeedbackType type)
    {
        try
        {
            HapticFeedback.Default.Perform(type);
        }
        catch (FeatureNotSupportedException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
    }
}
