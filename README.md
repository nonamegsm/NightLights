# NightLights

[![CI](https://github.com/nonamegsm/NightLights/actions/workflows/ci.yml/badge.svg)](https://github.com/nonamegsm/NightLights/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/nonamegsm/NightLights)](https://github.com/nonamegsm/NightLights/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A tiny Windows tray app that turns off your PC's extra RGB lighting after sunset
and turns it back on at sunrise - specifically:

- **Kingston FURY DIMM lighting**, controlled through FURY CTRL's own background
  service (`FuryControllerService.exe`) over its local WebSocket API.
- **MSI motherboard RGB ("Mystic Light")**, controlled through MSI's official
  Mystic Light SDK.

No admin rights needed, nothing is installed as a service, and there's no visible
window - just a tray icon. Sunrise/sunset is computed fully offline from your
latitude/longitude (NOAA solar formulas), so there's no network dependency at all.

## Installation

**Option 1 - Installer (recommended).** Grab `NightLights-Setup-*.exe` from the
[latest release](https://github.com/nonamegsm/NightLights/releases/latest)
and run it. It installs per-user (no admin prompt, no UAC), adds Start Menu /
optional desktop shortcuts, and a normal uninstaller in "Add or remove programs".

**Option 2 - Portable.** Grab `NightLights-portable-*.zip` from the same
release, unzip it anywhere, and run `NightLights.exe`. Nothing is installed or
written outside that folder and `%AppData%\NightLights`.

**Option 3 - Build from source.** See "Building it" below.

Either way, on first launch NightLights adds nothing to Windows startup unless
you tick "Start with Windows" from its tray menu yourself.

## Why it works this way

`OpenRGB` didn't reliably see these devices, and neither Kingston nor (for the
open API) MSI ship a lightweight "just turn it off" command-line tool. So:

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
4. Run it. A moon icon appears in the system tray.

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

## Using it

Right-click the tray icon:

- **Follow sun automatically** - the default; uses your configured
  latitude/longitude.
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
- **Settings...** - latitude/longitude, which lighting to control, and how
  often to check (default: every 60 seconds).
- **Open log folder** - `%AppData%\NightLights\NightLights.log`, plus the
  cached "day profile" snapshots, for troubleshooting.

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

1. Build the app in Release from `NightLights.sln`.
2. Install [Inno Setup](https://jrsoftware.org/isinfo.php).
3. Run `iscc installer\setup.iss` (or open it in the Inno Setup Compiler and
   click Compile). The installer is written to `dist\`.

To cut a new release yourself (if you've forked this): push a tag matching
`v*`, e.g. `git tag v1.0.1 && git push origin v1.0.1` - the release workflow
builds the app, the installer, and a portable zip, and publishes them as a
GitHub Release automatically.

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
  AppSettings.cs           settings persisted to %AppData%\NightLights\settings.json
  SunTimes.cs               offline NOAA sunrise/sunset calculator
  SettingsForm.cs/.Designer.cs   the Settings dialog
  Rgb/
    FuryCrypto.cs            AES-256 wire format used by FuryControllerService
    FuryLightController.cs   WebSocket client for FuryControllerService
    MysticLightController.cs P/Invoke wrapper for MSI's Mystic Light SDK
installer/
  setup.iss                Inno Setup installer script
.github/workflows/
  ci.yml                   build check on every push/PR
  release.yml              builds + publishes the installer and portable zip on a version tag
LICENSE, CHANGELOG.md, CONTRIBUTING.md, .gitignore
```
