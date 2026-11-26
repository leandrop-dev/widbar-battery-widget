# WidBar battery widget

A [WidBar](https://apps.microsoft.com/detail/9pkldnm83tp9) widget that shows the battery level of your wireless
devices on the Windows taskbar, with a flyout for detail and a settings page.

- SteelSeries headsets and mice (Arctis Nova 3/5/7, Arctis 7+, GameBuds, Rival 3 Wireless; Arctis Nova Pro Wireless is
  included but unverified: no unit was available to test it)
- PlayStation DualSense (over USB), Xbox-style controllers (coarse levels), and Bluetooth LE devices that expose the
  standard Battery Service (not yet verified inside WidBar)
- A bolt next to a device that is charging, a dimmed last value for a device that is asleep, and a "!" when a level is low
- Settings, laid out like WidBar's System Metrics widget with a live preview: layout (vertical meters, minimal text, minimal icons),
  value size, spacing, alignment and width, labels, percent symbol, separators and charging bolt, coloured labels and
  values, the Windows accent colour, battery colours (classic, colour-blind, pastel, high contrast, mono, custom),
  icon style for the minimal icons layout, which devices show and in what order, a name and colour per device, the low-battery level and the refresh interval.
  Options a layout does not draw are greyed out
- A flyout on click with a Summary tab and one tab per kind of device (headsets, mice, keyboards, controllers), showing each level, a level bar, its state and when it was last seen

It does not read PC load or temperatures: WidBar's own System Metrics widget covers those.

Built with C# / .NET 10 and WinUI 3, on the [WidBar widget SDK](https://github.com/andelby/widbar-widget-template).
The reader protocols follow [Gabrielfch/taskbar-battery-widget](https://github.com/Gabrielfch/taskbar-battery-widget);
see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Status: signed releases on GitHub (sideload, not in the Microsoft Store). The plugin id is `com.codemonk.devicebattery`.
Tested on Windows 11 with one monitor.

## Install

Download the `.msixbundle` and the `.cer` from the [latest release](../../releases/latest), trust the certificate once
(Install Certificate > Local Machine > Trusted People), open the bundle, then add **Device batteries** in WidBar.
To update, install the newer bundle over the old one.

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

Raise the `Version` in the manifest for every rebuild, and keep it above the installed one (a released 0.4.0.0
counts): Windows refuses to reinstall the same version or to go back. WidBar must be installed; add the widget from
its settings.

`-Arch` chooses what is built: `x64` (the default), `arm64`, or `both`, which produces one signed x64 + ARM64
`artifacts\DeviceBatteries.msixbundle`, the file a release publishes. The per-architecture publish profiles are in
`src\Widgets.WidBar\Properties\PublishProfiles`; the ARM64 package cannot be tried on an x64 PC.

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

## Release

Pushing a tag such as `v0.5.0` makes `.github/workflows/release.yml` test, stamp the version
(`build/Set-Version.ps1`), build the signed x64 + ARM64 bundle and publish a GitHub Release. Running the workflow
by hand with a version builds the bundle as an artifact without publishing. It needs the repository secrets
`SIGNING_CERT_BASE64` and `SIGNING_CERT_PASSWORD`; `build/New-ReleaseCert.ps1` creates the certificate and prints the
commands to upload them. Keep the certificate files: a new certificate means everyone must trust it again.
