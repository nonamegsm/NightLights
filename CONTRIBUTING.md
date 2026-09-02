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
- `NightLights/Rgb/FuryCrypto.cs` / `FuryLightController.cs` - the Kingston FURY CTRL WebSocket client.
- `NightLights/Rgb/MysticLightController.cs` - the MSI Mystic Light SDK wrapper.
- `installer/setup.iss` - the Inno Setup installer script.
- `.github/workflows/` - CI (build check) and release (installer + portable zip) automation.

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
