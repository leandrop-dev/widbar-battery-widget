# Development

Notes for building, packaging and releasing the widget. For using it, see the [README](../README.md).

## Build and test

You need the .NET 10 SDK, PowerShell 7 (`winget install Microsoft.PowerShell`; the build scripts and some tests use it)
and the [WinApp CLI](https://github.com/microsoft/WinAppCli) (`winget install Microsoft.WinAppCli`).

```powershell
dotnet test tests\Widgets.Core.Tests
dotnet test tests\Widgets.Providers.Tests
```

`dotnet test` takes one project per call.

## Layout

| Folder | Contents |
|---|---|
| `src/Widgets.Core` | Metric model, scheduler, appearance and widget settings, formatting (no UI, fully tested) |
| `src/Widgets.Providers` | Battery readers: SteelSeries and DualSense (HID), XInput, Bluetooth LE |
| `src/Widgets.WidBar` | The WidBar widget: taskbar tiles, flyout and settings page |

## Package and install a local build

The widget ships as a signed MSIX that WidBar finds through its `com.widbar.widget` extension. Create a self-signed
certificate whose subject matches the `Publisher` in `src\Widgets.WidBar\Package.appxmanifest` (`winapp cert generate`),
trust its public part (an administrator step), then:

```powershell
.\build\package-widbar.ps1 -Cert <path to the .pfx> -CertPassword <password of the .pfx>
Add-AppxPackage artifacts\DeviceBatteries.msix -ForceApplicationShutdown
```

`-Cert` defaults to `%LOCALAPPDATA%\WinTaskbarWidgets-dev\devcert.pfx`, so pass it unless the certificate is there.
Without `-ForceApplicationShutdown` the install fails with 0x80073D02, because WidBar restarts the widget process.

Raise the version for every rebuild and keep it above the installed one and above the latest release: Windows refuses
to reinstall the same version or to go back. Raise it with `.\build\Set-Version.ps1 -Version X.Y.Z`, which changes the
manifest, the project file and the plugin class together. WidBar must be installed; add the widget from its settings.

`-Arch` chooses what is built: `x64` (the default), `arm64`, or `both`, which produces one signed x64 + ARM64
`artifacts\DeviceBatteries.msixbundle`, the same bundle a release publishes (as `DeviceBatteries-<version>.msixbundle`).
The per-architecture publish profiles are in `src\Widgets.WidBar\Properties\PublishProfiles`. The ARM64 package
cannot be tried on an x64 PC.

## Release

Pushing a tag such as `v0.5.0` makes `.github/workflows/release.yml` test, stamp the version
(`build/Set-Version.ps1`), build the signed x64 + ARM64 bundle and publish a GitHub Release. The stamp is made only
inside the build, so after a release run `build\Set-Version.ps1 -Version <released version>` and commit the result to
keep the source in step. Running the workflow by hand with a version builds the bundle as an artifact without
publishing.

It needs the repository secrets `SIGNING_CERT_BASE64` and `SIGNING_CERT_PASSWORD`; `build/New-ReleaseCert.ps1` creates
the certificate and prints the commands to upload them. Keep the certificate files: a new certificate means everyone
must trust it again.
