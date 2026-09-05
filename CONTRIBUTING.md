# Contributing

Issues and PRs are welcome - this is a small, single-purpose utility, so please
keep changes focused.

## Building

Open `NightLights.sln` in Visual Studio 2022 with the ".NET desktop
development" workload (specifically the .NET Framework 4.8 targeting pack)
installed, and build. No NuGet packages are required.

## Where things live

- `NightLights/TrayContext.cs` - the tray icon, menu, and the day/night polling loop.
- `NightLights/SunTimes.cs` - offline sunrise/sunset math.
- `NightLights/NightSchedule.cs` - manual override and sun/quiet-hour schedule decisions.
- `NightLights/Rgb/FuryCrypto.cs` / `FuryLightController.cs` - the Kingston FURY CTRL WebSocket client.
- `NightLights/Rgb/MysticLightController.cs` - the MSI Mystic Light SDK wrapper.
- `NightLights/Rgb/ILightingModule.cs` - the common contract and existing vendor adapters.
- `NightLights/Rgb/LightingCoordinator.cs` - lighting transitions and restore retries.
- `NightLights/Rgb/OpenRgbController.cs` - the optional OpenRGB SDK protocol adapter.
- `NightLights/Power/` - reversible Windows Power saver policy and native API boundary.
- `NightLights.Tests/` - dependency-free regression runner with simulated hardware.
- `installer/setup.iss` - the Inno Setup installer script.
- `.github/workflows/` - CI (builds and regression tests) and release installer automation.

## Testing and adding modules

Run `msbuild NightLights.Tests/NightLights.Tests.csproj /p:Configuration=Release`,
then `NightLights.Tests/bin/Release/NightLights.Tests.exe`. These tests use fake power
APIs and simulated RGB servers, and do not require or change real hardware.

New lighting providers implement `ILightingModule`; snapshot/color-profile methods
save a daytime baseline, `TurnOffAsync` enforces darkness without replacing an existing
baseline, and `RestoreAsync` reapplies it. Add an explicit opt-in setting, wire the
provider into `TrayContext.EnabledLighting`, and include hardware-free failure and
restore tests. Return `false` on failure so the tray can report it. Keep vendor/native
calls out of the UI thread and avoid introducing package dependencies.

## If FURY CTRL changes its protocol

The Kingston side (`FuryCrypto.cs` / `FuryLightController.cs`) talks to a local,
undocumented API discovered by decompiling `FuryControllerService.exe` (see
"Disclaimer & legal" in the README) - Kingston could change it in a future
FURY CTRL update. If NightLights suddenly stops controlling your DIMMs after a
FURY CTRL update, check `%AppData%\NightLights\NightLights.log` first; if it
shows connection or decrypt failures against `127.0.0.1:55599`, that's the
likely cause, and a PR re-checking the protocol against the new
`FuryControllerService.exe` (a .NET assembly - `ilspycmd` or dotPeek gets you
close to original source) would be very welcome.

## Reporting a hardware compatibility issue

Please include: your motherboard model, which Kingston FURY modules you have,
what happened (with a snippet of `NightLights.log`), and whether FURY CTRL /
MSI Center themselves control the lighting correctly outside of NightLights.
