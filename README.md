# WidBar battery widget

A [WidBar](https://apps.microsoft.com/detail/9pkldnm83tp9) widget that shows the battery level of your wireless
devices on the Windows taskbar, with a flyout for detail and a settings page.

- SteelSeries headsets and mice (Arctis Nova 3/5/7, Arctis 7+, GameBuds, Rival 3 Wireless; Arctis Nova Pro Wireless is
  included but unverified: no unit was available to test it)
- PlayStation DualSense (over USB), Xbox-style controllers (coarse levels), and Bluetooth LE devices that expose the
  standard Battery Service (not yet verified inside WidBar)
- A bolt next to a device that is charging, a dimmed last value for a device that is asleep, and a "!" when a level is low
- Settings: which devices show and in what order, icon style (outline, filled, simple, battery gauge, none), battery
  colours (classic, colour-blind, pastel, high contrast, mono, custom), size, device names, and the low-battery level
- A flyout on click with every device's level, a level bar and its state

It does not read PC load or temperatures: WidBar's own System Metrics widget covers those.

Built with C# / .NET 10 and WinUI 3, on the [WidBar widget SDK](https://github.com/andelby/widbar-widget-template).
The reader protocols follow [Gabrielfch/taskbar-battery-widget](https://github.com/Gabrielfch/taskbar-battery-widget);
see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Status: sideloaded builds only. The plugin id (`com.example.devicebatteries`) is a placeholder to replace before any
public release. Tested on Windows 11 with one monitor.

## Build

Needs the .NET 10 SDK and the [WinApp CLI](https://github.com/microsoft/WinAppCli) (`winget install Microsoft.WinAppCli`).

```powershell
dotnet test tests\Widgets.Core.Tests
dotnet test tests\Widgets.Providers.Tests
```

## Package and install

The widget ships as a signed MSIX that WidBar finds through its `com.widbar.widget` extension. Create a self-signed
certificate whose subject matches the `Publisher` in `src\Widgets.WidBar\Package.appxmanifest` (`winapp cert generate`),
trust its public part (an administrator step), then:

```powershell
.\build\package-widbar.ps1 -CertPassword <password of the .pfx>
Add-AppxPackage artifacts\DeviceBatteries.msix
```

Raise the `Version` in the manifest for every rebuild: Windows refuses to reinstall the same version.
WidBar must be installed; add the widget from its settings.

## Layout

| Folder | Contents |
|---|---|
| `src/Widgets.Core` | Metric model, scheduler, appearance and widget settings, formatting (no UI, fully tested) |
| `src/Widgets.Providers` | Battery readers: SteelSeries and DualSense (HID), XInput, Bluetooth LE |
| `src/Widgets.WidBar` | The WidBar widget: taskbar tiles, flyout and settings page |

## Privacy

Everything stays on the PC. Nothing is sent over the network. The last known battery levels are cached in the widget's
shared data folder.

## Licence

MIT, see [LICENSE](LICENSE). Third-party components and the ported code are covered in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
