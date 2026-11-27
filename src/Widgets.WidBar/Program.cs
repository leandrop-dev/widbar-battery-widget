using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Runtime.InteropServices;
using WinRT;

namespace Widgets.WidBar;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();

        // Clicking a low-battery notification starts this exe again, outside WidBar. That would only show the "WidBar is
        // required" window, so the notification is informational: the extra process exits and nothing opens.
        if (IsNotificationClick()) return;

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    private static bool IsNotificationClick()
    {
        try
        {
            return AppInstance.GetCurrent().GetActivatedEventArgs().Kind
                is ExtendedActivationKind.ToastNotification or ExtendedActivationKind.AppNotification;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return false; // cannot tell: start normally, as before
        }
    }
}
