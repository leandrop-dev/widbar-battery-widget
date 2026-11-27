using System.Runtime.InteropServices;
using Widgets.Core;
using Windows.UI.Notifications;

namespace Widgets.WidBar;

/// <summary>Shows the low-battery notifications. A notification that cannot be shown is dropped: the tile still says it.</summary>
internal static class Notifier
{
    /// <summary>The marker carried by our notifications, so <see cref="Program"/> can tell a click on one from a normal launch.</summary>
    public const string LaunchMarker = "widbar-battery-alert";

    public static void Low(Alert alert) =>
        Show("Low battery", $"{alert.Label} is at {alert.Percent}%. Charge it soon.");

    private static void Show(string title, string body)
    {
        try
        {
            var xml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var lines = xml.GetElementsByTagName("text");
            lines[0].AppendChild(xml.CreateTextNode(title));
            lines[1].AppendChild(xml.CreateTextNode(body));
            xml.DocumentElement.SetAttribute("launch", LaunchMarker);
            ToastNotificationManager.CreateToastNotifier().Show(new ToastNotification(xml));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
        }
    }
}
