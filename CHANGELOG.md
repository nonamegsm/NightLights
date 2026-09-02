# Changelog

All notable changes to this project are documented here.
Format loosely follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

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
