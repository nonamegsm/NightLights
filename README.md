# NightLights

[![CI](https://github.com/nonamegsm/NightLights/actions/workflows/ci.yml/badge.svg)](https://github.com/nonamegsm/NightLights/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/nonamegsm/NightLights)](https://github.com/nonamegsm/NightLights/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A small Windows tray app that reduces nighttime RGB light and can switch your PC
to Windows Power saver. Follow sunset/sunrise, set quiet hours, or combine both.
Choose which modules to enable:

- **Kingston FURY DIMM lighting**, controlled through FURY CTRL's own background
  service (`FuryControllerService.exe`) over its local WebSocket API.
- **MSI RGB devices ("Mystic Light")**, controlled through MSI's official
  Mystic Light SDK.
- **OpenRGB devices** (optional) through an OpenRGB SDK server: discover compatible
  devices, switch lighting off at night, and restore saved colors/modes during the day.
- **Windows Power saver** (optional) - remembers the active power plan before night
  mode and restores it when night mode ends.
- **System volume** (optional, off by default) - mutes Windows audio at night and
  unmutes it again at sunrise, via the same public Core Audio API the volume
  mixer's own mute button uses.

No admin rights needed, nothing is installed as a service, and there's no visible
window - just a tray icon. Sunrise/sunset is computed fully offline from your
latitude/longitude (NOAA solar formulas). Scheduling needs no internet connection;
OpenRGB connects only to the SDK server address you configure (localhost by default).

## Hardware support at a glance

See [HARDWARE.md](HARDWARE.md) for the full supported-hardware guide,
OpenRGB upstream links, setup requirements, and the difference between native
NightLights integrations and OpenRGB-bridged devices.

| Area | Path | Required setup | NightLights behavior |
| --- | --- | --- | --- |
| Kingston FURY DIMM RGB | Native FURY CTRL service | FURY CTRL service on `127.0.0.1:55599` | Save, turn off at night, restore, or set a static day color for service-reported DIMM lighting. |
| MSI Mystic Light devices | Native MSI SDK | MSI Center/Mystic Light, SDK enabled, `MysticLight_SDK.dll` next to `NightLights.exe` | Save and restore SDK-reported LED zone colors; set zones to black at night. |
| OpenRGB devices | OpenRGB SDK server | OpenRGB controls the device, SDK Server running, NightLights pointed at host/port | Control SDK-reported devices with usable direct/custom/static modes. Use **Test connection / list devices** to see per-device status. |
| Windows Power saver | Windows power-plan API | An available Power saver plan and Windows policy allowing plan changes | Switch to Power saver at night and restore the previous plan in the morning. |
| System audio | Windows Core Audio endpoint | A default playback endpoint | Mute at night and unmute when day mode returns. |

## Installation

**Installer (recommended).** Grab the installer matching your Windows install from the
[latest release](https://github.com/nonamegsm/NightLights/releases/latest) and run it:

- `NightLights-Setup-x64-*.exe` - 64-bit Windows (almost every PC bought since ~2010;
  also required if you want MSI motherboard RGB control, since MSI only ships an x64
  Mystic Light SDK).
- `NightLights-Setup-x86-*.exe` - 32-bit Windows.

It installs per-user (no admin prompt, no UAC), adds Start Menu / optional desktop
shortcuts, and a normal uninstaller in "Add or remove programs". (The release page
also always shows GitHub's own auto-generated "Source code (zip/tar.gz)" links under
"Assets" - those are just the repo's source, not something to run; the two installer
`.exe` files above are the actual downloads.)

**Build from source** is the other option - see "Building it" below.

Either way, on first launch NightLights adds nothing to Windows startup unless
you tick "Start with Windows" from its tray menu yourself.

## Why it works this way

The original Kingston/MSI integrations cover devices that OpenRGB did not reliably
detect. They remain available alongside the optional OpenRGB module:

- **MSI side**: uses MSI's own, publicly documented Mystic Light SDK
  (`MysticLight_SDK.dll` + the P/Invoke calls below) - this is the same SDK
  third-party tools like MSI-mystic-light-tool use. Nothing reverse engineered here.
- **Kingston side**: FURY CTRL ships a background Windows service,
  `FuryControllerService.exe`, that stays running and listens on
  `ws://127.0.0.1:55599/` - the FURY CTRL GUI itself is just a client that talks
  to this service in JSON, wrapped in AES-256 encryption. That protocol isn't
  publicly documented, so it was recovered by decompiling
  `FuryControllerService.exe` (a .NET assembly - straightforward to read back to
  near-original C#) purely to *interoperate* with software you already own and
  that's already running elevated on your own PC - the same category of reverse
  engineering that projects like OpenRGB do routinely for hardware support.
  NightLights doesn't touch the SMBus/driver layer at all; it just sends the
  already-running, already-licensed FURY CTRL service the same commands its own
  GUI would send.

This means: if Kingston changes the protocol in a future FURY CTRL update, the
Kingston half of this app may stop working until it's re-checked against the new
version - it'll just log a note and skip that part rather than crash.

## Building it

1. Open `NightLights.sln` in Visual Studio 2022.
2. Make sure the **.NET Framework 4.8 targeting pack** is installed (Visual
   Studio Installer -> Individual components -> ".NET Framework 4.8 targeting
   pack" - it's included by default with the ".NET desktop development"
   workload).
3. Build (Ctrl+Shift+B). No NuGet packages are required - everything used is a
   built-in .NET Framework assembly.
4. Run it. A sun (day mode) or moon (night mode) appears in the system tray.

*(.NET Framework 4.8 was chosen deliberately, not .NET 5+/8: the encryption
FuryControllerService uses is Rijndael with a 256-bit block size, which the
classic .NET Framework crypto stack supports natively but modern .NET's
CNG-backed crypto providers reject with "Specified block size is not valid for
this algorithm." Re-implementing 256-bit-block Rijndael by hand to support
modern .NET wasn't worth the risk of getting it subtly wrong.)*

## Setting up the MSI motherboard side (optional)

1. Make sure **MSI Center** (or the standalone Mystic Light app) is installed,
   and that **Mystic Light SDK** is switched on in its settings (Settings ->
   SDK -> Mystic Light SDK, in MSI Center). This is what lets any external app
   talk to your board's RGB at all.
2. Download the **Mystic Light SDK** from MSI:
   <https://storage-asset.msi.com/file/pdf/Mystic_Light_Software_Development_Kit.pdf>
   (the SDK reference doc; the actual download bundle is linked from MSI's
   Mystic Light SDK page) and copy `MysticLight_SDK.dll` (the x64 build) next
   to `NightLights.exe`.
3. That's it - NightLights will pick it up automatically. If the DLL isn't
   there, or MSI Center's SDK toggle is off, NightLights just logs a note and
   skips the motherboard RGB - the Kingston DIMM control still works fine on
   its own.

## Kingston FURY side

Nothing to set up - as long as FURY CTRL is installed (its background service
starts automatically with Windows), NightLights will find it on
`127.0.0.1:55599`.

## OpenRGB module (optional)

1. Install [OpenRGB](https://openrgb.org/) and check that it controls your devices.
2. Start its **SDK Server**, normally at `127.0.0.1:6742`.
3. In NightLights **Settings > Lighting modules**, enable **OpenRGB devices**.
   Set the server host/IP and port, then use **Test connection / list devices**.
4. Disable the FURY or MSI module if OpenRGB is also controlling the same hardware.
   The OpenRGB module applies to all compatible devices on the chosen server.
5. Save your daytime lighting using the tray's **Save current lighting as day profile**
   or **Set day profile color...** command.

NightLights uses SDK protocol 3 (OpenRGB 0.7 or later). It supports devices with a
usable direct/custom/static/solid/fixed lighting mode or advertised per-LED or
mode-specific color control. The connection test lists device types, LED counts,
compatible modes, and reasons for unsupported devices. Hardware detection and device-specific
drivers remain OpenRGB's responsibility; unsupported devices are reported in the
device list/log. See [HARDWARE.md](HARDWARE.md#openrgb-supported-devices) for
the upstream OpenRGB support list and examples of devices that may or may not
expose the modes NightLights needs.
Day profiles are stored separately for each server under `%AppData%\NightLights`,
and devices are matched by identity when restoring after the server's device order changes.
Keep other RGB apps/effect plugins from overriding the same devices.

## Nighttime energy saving and quiet hours

In **Settings > Night schedule**, choose one of:

- **Sunset to sunrise**: uses your coordinates, including polar day/night.
- **Quiet hours**: fixed local start/end times, including periods that cross midnight
  (for example, 22:00 to 07:00). Start is inclusive; end is exclusive.
- **Sunset to sunrise or quiet hours**: night mode stays active while either applies.

In **Settings > Energy and startup**, enable **Use Windows Power saver during night
mode**. It is off by default. At the next night decision the app records your current
plan and activates the existing Windows Power saver plan. When night mode ends,
when you disable the setting, or when you exit NightLights, it restores the saved
plan. Recovery information is kept on disk across restarts and failed restore attempts.
If you manually choose another power plan at night, NightLights respects that choice.

This uses the plan's existing display/sleep timeouts; it does not change those values,
request immediate sleep/hibernate, or wake a sleeping PC at sunrise. After resume it
rechecks the schedule and restores the plan if daytime has begun. A PC without an
available Power saver plan, or with policy restrictions, reports the failure in the
tray menu/log; lighting modules continue working. Power changes use Microsoft's
[PowerGetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powergetactivescheme)
and [PowerSetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme) APIs.

Turning decorative RGB off reduces unwanted nighttime light. Actual electricity
savings depend on the hardware, workload, and power-plan configuration; NightLights
does not estimate watts, carbon emissions, or ambient light levels.

## Using it

The tray icon follows the active mode: **gold sun for day**, **blue crescent moon
for night**. It updates at startup, on schedule transitions, after resume, and when
you choose Force day / Force night or change the schedule in Settings. The tooltip
also identifies automatic or forced mode. Hardware availability is shown separately
in the tray menu; the icon reflects the mode decision even if a device is unavailable.

Right-click the tray icon:

- **Follow schedule automatically** - uses your configured sun/quiet-hour schedule;
  sunset to sunrise is the default.
- **Force night / Force day now** - manual override, e.g. if you want the
  lights off right now regardless of the clock.
- **Save current lighting as day profile** - snapshots whatever colors/effects
  are active right now as "the daytime look" to restore at the next sunrise.
  (This also happens automatically at every sunset, just before the lights
  turn off.)
- **Set day profile color...** - opens a color + brightness picker and sets
  that as the day profile for both the DIMMs and the motherboard RGB (whichever
  you have enabled), without needing to open FURY CTRL's own GUI. Brightness is
  sent explicitly on purpose - FURY CTRL's own service defaults a missing
  brightness value to 0 (i.e. off), so this is what actually makes the color
  visible. Applies immediately if it's currently day, or stays correctly off if
  you pick a color during the night - either way, it's what gets restored at
  the next sunrise.
- **Start with Windows** - adds/removes a per-user startup entry (no admin
  needed).
- **Settings...** - schedule and coordinates, lighting modules, nighttime power/audio
  options, startup, and how often to check (default: every 60 seconds).
- The tray menu also shows lighting availability and the current power-module status.
- **Open log folder** - `%AppData%\NightLights\NightLights.log`, plus the
  cached "day profile" snapshots, for troubleshooting.

**A note on the volume option:** unlike the lighting (which gets re-applied on
every poll while it's night, to fight FURY CTRL/some MSI boards silently
reloading their own last profile - see below), volume is only muted/unmuted
once, right when day/night actually changes. Windows doesn't spontaneously
un-mute itself the way those services do, so re-sending "mute" every poll
would just fight you if you manually unmute at night to hear something -
NightLights won't re-mute you until the next real transition (sunrise, or
another Force night/day).

## Why the lights don't silently turn back on

FuryControllerService (and apparently some MSI boards' embedded controller too)
can reload their own "last known" lighting profile on their own - most
noticeably a little while after the PC wakes from sleep, but it's not strictly
tied to that. To not lose the fight against this:

- While it's night, NightLights re-sends the "off" command on **every** poll
  (not just once at sunset) - so even if the device quietly turns itself back
  on between checks, it gets turned off again within one poll interval.
  Lower "Check every (seconds)" in Settings (minimum 15s) for tighter
  enforcement, at the cost of slightly more chatter on the local WebSocket/SDK
  calls (still negligible - everything here is loopback/local).
- NightLights also listens for the OS resume-from-sleep event directly and
  re-asserts the current day/night state about 10 seconds after waking (giving
  FuryControllerService and the motherboard EC time to reinitialize first),
  rather than waiting for the next scheduled poll.
- Either way, this re-assertion never touches the cached "day profile"
  snapshot - only the sunset transition (or the manual "Save current lighting
  as day profile") does that, so it can't get overwritten by an "all off"
  state.

## Building an installer yourself

You don't need to do this to use NightLights - every tagged release is built
automatically by GitHub Actions (`.github/workflows/release.yml`) and attached
to the release page. To build one locally anyway:

1. Build the app in Release, for the platform you want - in Visual Studio's
   Configuration Manager, or `msbuild NightLights.sln /p:Configuration=Release /p:Platform=x64`
   (or `x86`).
2. Install [Inno Setup](https://jrsoftware.org/isinfo.php).
3. Run `iscc installer\setup.iss /DBuildArch=x64` (or `/DBuildArch=x86`, matching
   whichever platform you built) - or open it in the Inno Setup Compiler and click
   Compile (defaults to x64). The installer is written to `dist\`.

To cut a new release yourself (if you've forked this): push a tag matching
`v*`, e.g. `git tag v1.0.1 && git push origin v1.0.1` - the release workflow
builds both x86 and x64, and publishes both installers as a GitHub Release
automatically.

## Disclaimer & legal

- **Not affiliated with Kingston or MSI.** "Kingston", "FURY", "FURY CTRL",
  "MSI", and "Mystic Light" are trademarks of their respective owners. This is
  an independent, community project with no official relationship to either
  company.
- **What's reverse engineered, and why.** The Kingston half of this app talks
  to `FuryControllerService.exe`'s local, undocumented WebSocket API. That
  protocol (endpoint, message shape, encryption scheme) was recovered by
  decompiling the already-installed `FuryControllerService.exe` on a machine
  that legitimately owns a FURY CTRL license, solely to *interoperate* with
  software the user already runs, elevated, on their own PC - the same
  category of reverse engineering that projects like
  [OpenRGB](https://openrgb.org/) do routinely for RGB hardware support, and a
  practice generally recognized as legitimate for interoperability purposes
  (e.g. under the EU Software Directive and the US DMCA's interoperability
  exemption, 17 U.S.C. §1201(f)).
- **No proprietary code or binaries are included.** This repository contains
  only independently written C# that implements the *interoperability
  information* (wire format, command shape) discovered that way - never any
  of Kingston's or MSI's own source, binaries, or decompiled code.
  `MysticLight_SDK.dll` is MSI's proprietary DLL and is never bundled;
  see "Setting up the MSI motherboard side" above for where to get it directly
  from MSI.
- **No warranty, use at your own risk.** This talks to a background service
  that also controls DRAM training/timing metadata for Kingston FURY modules;
  while NightLights only ever sends lighting-mode commands (never touches
  timings/voltages), the protocol is undocumented and could change without
  notice in a future FURY CTRL update. See [LICENSE](LICENSE) (MIT) for the
  full "as is" terms.
- Questions, protocol changes, or takedown requests: please open an issue
  (or, for a private concern, contact the repository owner directly) rather
  than assuming bad intent - this project exists purely so people can turn
  their own hardware's lights off at night.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). See [CHANGELOG.md](CHANGELOG.md) for
release history.

## License

[MIT](LICENSE) - see the license file for the short note on what is and isn't
included here regarding Kingston's and MSI's own software.

## Files

```
NightLights.sln
NightLights/
  Program.cs              entry point, tray-only (no main window)
  TrayContext.cs           tray icon, menu, day/night polling loop
  TrayModeIcons.cs         cached sun/moon icons for the active mode
  AppSettings.cs           settings persisted to %AppData%\NightLights\settings.json
  SunTimes.cs               offline NOAA sunrise/sunset calculator
  NightSchedule.cs          sun / quiet-hour / combined automatic policy
  SettingsForm.cs/.Designer.cs   the Settings dialog
  DayProfileColorForm.cs    color + brightness picker for "Set day profile color..."
  Rgb/
    FuryCrypto.cs            AES-256 wire format used by FuryControllerService
    FuryLightController.cs   WebSocket client for FuryControllerService
    MysticLightController.cs P/Invoke wrapper for MSI's Mystic Light SDK
    ILightingModule.cs       common lighting module contract and vendor adapters
    LightingCoordinator.cs   snapshot, transition, night enforcement, restore retries
    OpenRgbController.cs     optional OpenRGB TCP SDK client
    OpenRgbHardware.cs       device capabilities and hardware support reporting
  Power/
    PowerPlanController.cs   reversible night Power saver policy and recovery state
    WindowsPowerSchemeApi.cs Windows power-plan API adapter
  Audio/
    SystemVolumeController.cs COM interop wrapper for the Core Audio API (mute/unmute)
installer/
  setup.iss                Inno Setup installer script
.github/workflows/
  ci.yml                   build check on every push/PR
  release.yml              builds + publishes the x86 and x64 installers on a version tag
LICENSE, CHANGELOG.md, CONTRIBUTING.md, .gitignore
```

Hardware-free regression tests live in `NightLights.Tests`. From a Visual Studio
Developer PowerShell, run:

```powershell
msbuild NightLights.Tests/NightLights.Tests.csproj /p:Configuration=Release
./NightLights.Tests/bin/Release/NightLights.Tests.exe
```

They exercise schedules, settings, lighting transitions, OpenRGB protocol behavior,
and power-plan recovery using fake devices/APIs. They never switch the host's power
plan or send commands to physical RGB devices.
