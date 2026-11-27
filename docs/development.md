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
| `src/Widgets.Providers` | Battery readers: HID devices (SteelSeries, Sony, Logitech, Razer, Corsair, HyperX), XInput, Bluetooth LE |
| `src/Widgets.WidBar` | The WidBar widget: taskbar tiles, flyout and settings page |

## Adding a reader

A reader is a class that implements `IBatteryReader` in `src/Widgets.Providers/Battery`, with its protocol helpers (byte
layouts, tables, decoding) in a folder of their own as pure functions, so they can be tested without a device. Take the
`IHidSystem` in the constructor; `IHidHandle` offers read, write, feature report and input report calls.

- **Requests come from a table.** Build every request from a fixed table that refuses anything else, and check the frame
  again just before writing it. A reader reads status and never writes a setting. If a protocol cannot be read without a
  state-changing command, leave that command out and say so in the README.
- **Failure is quiet.** `ReadAsync` returns an empty list on any failure and throws only when the poll itself is
  cancelled. A device that stays silent gives no reading, so its last level is kept; report it as disconnected only
  when the protocol answers that explicitly.
- **Keep to a budget.** The readers run in parallel inside the 5 second poll. Give each device its own budget (the
  existing readers use 1.5 seconds or less) and never share a handle with another reader.
- **Register it twice.** Add its static `Supports(vendorId, productId)` to `SupportedDevices`, which the "request
  support" report uses to tell known devices from missing ones, and add it to the list in `BatteryHub.Start`. When two
  readers give the same id, the later one wins.
- **Least certain protocols** take a `Func<bool>` and read only when `BatteryHub.ExperimentalReaders` says so. That is
  the "Experimental device readers" switch, which is off by default.
- **Test with a simulator.** `RecordingHandle` and `RecordingHidSystem` in `tests/Widgets.Providers.Tests/TestSupport.cs`
  script a device's replies and record everything sent to it. Assert that every frame written is on the table, and
  cover the happy path, a disconnected device, a silent one, a collection of the wrong shape and the experimental gate.
  Then break the code on purpose (drop the gate, the cache, the charging rule) and check that a test fails.
- **Credit the source.** Port only from sources under a licence that allows it, such as the MIT project in
  `THIRD_PARTY_NOTICES.md`, and copy nothing from GPL projects. Read the upstream file at a pinned commit rather than
  from memory, add a row to the notices, and add the device to the README table as not tested until someone confirms it.

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
