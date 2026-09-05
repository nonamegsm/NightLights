# Changelog

All notable changes to this project are documented here.
Format loosely follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.1.2] - 2026-09-05

### Added
- Tray icon changes with the active mode: a gold sun for day and blue crescent moon
  for night, including scheduled changes, manual overrides, startup, and resume.

### Changed
- Mode icon and tooltip update before hardware operations, so slow or unavailable
  lighting controllers do not delay the displayed mode.
- Sun/moon icons are cached and their native resources are released when the app exits.

## [1.1.1] - 2026-09-05

### Added
- Supported-hardware guide with native/bridged support matrix, upstream device
  examples, prerequisites, limitations, and links to OpenRGB's model list.
- Per-device OpenRGB report with category, LED count, compatible mode, and an
  explanation for unsupported or ambiguously identified devices.
- OpenRGB color-mode recognition for Solid/Fixed names and advertised per-LED or
  mode-specific color capabilities, including differently named device modes.
- Hardware guide link in Settings and an offline copy included in installers.

### Fixed
- Explicitly select deterministic colors when a mode also offers random colors.
- Apply the selected night mode after updating LED colors, without relying on a
  vendor's interpretation of the generic custom-mode command.
- Skip invalid LED/color buffers and duplicate identities, report partial blackout
  accurately, and preserve unsupported-device baselines when setting a day color.

## [1.1.0] - 2026-09-05

### Added
- Optional OpenRGB SDK lighting module with device discovery, night-off commands,
  server-specific day profiles, and identity-based color/mode restoration.
- Quiet-hour and combined sun/quiet-hour schedules, with local-time midnight support.
- Optional Windows Power saver at night, preserving and restoring the original plan
  at daytime/disable/exit with durable recovery state and respect for manual changes.
- Tabbed Settings for schedules, lighting modules, and energy/startup options.
- Hardware-free regression test runner and CI coverage for scheduling, settings,
  lighting transitions, power recovery, and the OpenRGB protocol.

### Fixed
- Manual night transitions now save the daytime profile before lights are turned off.
- Tray actions are serialized and UI updates remain on the Windows Forms thread.
- Failed daytime lighting restores retry on subsequent polls.
- Polar night is distinguished from polar day; settings loaded from JSON are bounded
  before being used by controls and timers.

## [1.0.1] - 2026-09-05

### Changed
- Release now builds and publishes separate `NightLights-Setup-x86-*.exe` and
  `NightLights-Setup-x64-*.exe` installers (dropped the AnyCPU-only portable zip) - CI
  now also compiles all three platform configs (Any CPU, x86, x64) on every push.

### Added
- Optional "Silence system volume at night" setting: mutes Windows audio at sunset/"Force
  night" and unmutes it at sunrise/"Force day", via the public Core Audio API. Off by
  default. Applied once per real transition (not re-sent every poll like the lighting), so
  a manual unmute at night sticks until the next transition.
- "Set day profile color..." tray menu item: pick one solid color from within the app to
  use as the day profile for the DIMMs and/or motherboard RGB, without needing FURY CTRL's
  own GUI.

### Fixed
- Corrected installer source and icon paths so both Windows setup executables build.
- Package only the application and its configuration; never include local vendor DLLs.
- `AppSettings` is now `public` (was `internal`), fixing a build error where `SettingsForm`'s
  public constructor/`Result` property exposed a less-accessible type.
- "Force night/day now", "Follow sun automatically", and closing Settings now always actually
  apply the resulting state (turn lights off, or restore the day profile) instead of sometimes
  only re-snapshotting - previously, forcing day right after forcing night could silently do
  nothing and overwrite the cached day profile with the "lights off" state.
- While it's night, the "off" command is now re-sent on every poll (not just once at sunset),
  and a resume-from-sleep listener re-asserts the current state a few seconds after wake -
  FuryControllerService (and some MSI boards) can silently reload their own last-known
  lighting profile on their own schedule, most noticeably after sleep.

## [1.0.0] - Unreleased

### Added
- Tray-only WinForms app (.NET Framework 4.8), no visible window, no admin rights required.
- Offline sunrise/sunset calculation (NOAA solar algorithm) from a configurable latitude/longitude.
- Kingston FURY DIMM lighting control via FuryControllerService's local WebSocket API (`ws://127.0.0.1:55599`), including AES-256 request/response encryption matching the service's own scheme.
- MSI motherboard RGB control via MSI's official Mystic Light SDK.
- Automatic "day profile" snapshot/restore: captures current lighting at sunrise (or on demand) and restores it, rather than a fixed color.
- Continuous re-assertion of the "off" state throughout the night (not just once at sunset), since FURY CTRL's own service can silently reload its last "kept" profile on its own schedule.
- Resume-from-sleep handling: re-applies the current day/night state a few seconds after the PC wakes, rather than waiting for the next poll.
- Tray menu: follow sun automatically / force night / force day, save current lighting as day profile, start with Windows, settings dialog, open log folder.
- Inno Setup installer (per-user, no admin required) and a portable zip, both built automatically by GitHub Actions on tagged releases.
