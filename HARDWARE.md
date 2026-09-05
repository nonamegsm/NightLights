# Supported hardware

NightLights controls hardware through three lighting paths:

- **Kingston FURY native module**: talks to the local FURY CTRL background
  service at `ws://127.0.0.1:55599/`.
- **MSI Mystic Light native module**: calls MSI's `MysticLight_SDK.dll`.
- **OpenRGB module**: connects to an OpenRGB SDK server, normally
  `127.0.0.1:6742`, and controls the devices that OpenRGB reports.

The OpenRGB path is the broad compatibility path. NightLights does not include
OpenRGB's hardware drivers and does not probe hardware directly through SMBus,
I2C, USB HID, or vendor drivers. It asks the configured SDK or service what is
present, then uses only the control modes that the SDK reports as usable.

## Quick support matrix

| Hardware area | NightLights path | Requirement | What NightLights can do |
| --- | --- | --- | --- |
| Kingston FURY DIMM RGB | Native Kingston FURY module | FURY CTRL installed and its local service running on `127.0.0.1:55599` | Save the current DIMM lighting, turn it off at night, restore it during the day, or set a static day color. |
| MSI Mystic Light devices | Native MSI module | MSI Center or Mystic Light installed, Mystic Light SDK enabled, and `MysticLight_SDK.dll` copied next to `NightLights.exe` | Save LED zone colors, set zones to black at night, restore colors during the day, or set a static day color. |
| OpenRGB-supported RGB devices | OpenRGB module | OpenRGB installed, target devices working in OpenRGB, SDK Server enabled, and NightLights pointed at the host/port | Save colors/modes, switch compatible devices off at night, restore saved lighting, or apply a static day color. |
| Windows power plans | Windows power-plan API | Windows has an available Power saver plan and policy allows switching plans | Switch to Power saver at night and restore the previous plan in the morning. This is a Windows feature, not a hardware compatibility promise. |
| System audio mute | Windows Core Audio endpoint API | A default Windows playback endpoint exists | Mute at night and unmute when day mode returns. This affects the default playback endpoint, not individual speakers or headsets as RGB hardware. |

NightLights itself targets Windows with .NET Framework 4.8. Release installers
are built for x64 and x86 Windows. The x64 installer is required for the MSI
module because MSI ships the Mystic Light SDK DLL for x64 use.

## OpenRGB-supported devices

OpenRGB keeps the authoritative device list. Use these upstream pages instead
of treating this document as a full model database:

- [OpenRGB Supported Devices (1.0rc3)](https://openrgb.org/devices_1.0rc3.html)
- [OpenRGB supported device CSV](https://openrgb.org/data/supported_devices_1.0rc3.csv)
- [OpenRGB SDK overview](https://openrgb.org/sdk.html)

As of the OpenRGB 1.0rc3 list, OpenRGB has entries across these categories:
graphics cards, motherboards, RAM sticks, mice, keyboards, mousemats, coolers,
LED strips, headsets and headset stands, gamepads, accessories, microphones,
speakers, storage, monitors, and cases.

NightLights support depends on the device data returned by the OpenRGB SDK
server. A device being listed by OpenRGB means OpenRGB has a driver entry for
it; it does not automatically mean NightLights can black it out and restore it.
NightLights needs addressable LEDs with matching LED/color counts and either a
usable `Direct`, `Custom`, `Static`, `Solid`, or `Fixed` mode, or a mode advertising
OpenRGB's per-LED or mode-specific color capabilities. Random-only effects and
ambiguous duplicate device identities are reported as unavailable. Some
OpenRGB entries expose only effect modes, partial support, automatic saving, or
device-specific caveats.

NightLights uses OpenRGB SDK protocol 3, matching the README requirement of
OpenRGB 0.7 or later.

Use **Settings > Lighting modules > Test connection / list devices** as the
local source of truth. The device report shows each OpenRGB device name, vendor,
device type, LED count, mode/control status, and the reason when NightLights
cannot control it for night lighting. NightLights does not probe beyond the
OpenRGB SDK response.

**Supported hardware guide** in the same tab opens this document on GitHub.
Installers also include `HARDWARE.md` beside `NightLights.exe` for offline reading.
Compatibility is inferred from the reported SDK data; the read-only connection
test does not turn lights off or prove a particular physical device works.

### Upstream examples

These examples come from the official OpenRGB 1.0rc3 supported-device CSV. They
show the kinds of devices OpenRGB can expose to NightLights; they are not a
physical NightLights test matrix.

| Example upstream entry | OpenRGB category | OpenRGB transport | OpenRGB Direct | Notes for NightLights |
| --- | --- | --- | --- | --- |
| `ASUS ROG STRIX GeForce RTX 2060 Gaming` | GPU | SMBus | Yes | Expected to be controllable when the SDK reports the GPU with a compatible mode. |
| `Kingston Fury DDR4/5 DRAM` controller entry | RAM | SMBus | Yes | This is OpenRGB-bridged RAM support, separate from NightLights' native FURY CTRL path. |
| `HyperX DRAM` controller entry | RAM | I2C | Yes | OpenRGB-bridged support depends on OpenRGB detecting the module and reporting usable modes. |
| `AMD Wraith Prism` | Cooler | USB | Yes | OpenRGB marks effects as partial; NightLights only needs static/direct-style night control. |
| `Corsair Lighting Node Pro` | LED strip controller | USB | Yes | Controls strips through OpenRGB when OpenRGB reports the controller and LED zones. |
| `NZXT Hue 2` | LED strip controller | USB | Yes | Controls strips through OpenRGB when the SDK reports addressable LEDs. |
| `Razer Blackwidow Chroma` / Razer controller entry | Keyboard and other Razer device classes | USB | Yes | OpenRGB lists the controller across multiple Razer classes; NightLights relies on the actual SDK device report. |
| `ASRock Polychrome USB` | Motherboard | USB | No | OpenRGB lists effects support, but NightLights may report it unsupported if no static/direct-compatible mode is present. |

For best results, first confirm the device works in OpenRGB itself, then start
the SDK Server and test from NightLights. If another RGB app is also controlling
the same hardware, close it or disable the overlapping NightLights module.

## Kingston FURY native module

The native Kingston module talks to `FuryControllerService.exe`, the local
background service installed by FURY CTRL. Kingston's FURY CTRL page says FURY
CTRL customizes Kingston FURY RGB products, works with DDR4 and DDR5, and also
supports legacy HyperX RGB memory:

- [Kingston FURY CTRL](https://www.kingston.com/en/gaming/fury-ctrl)

NightLights does not use Kingston's hardware driver directly. It sends lighting
commands to the already-running service over the local WebSocket endpoint. The
current NightLights code reads and writes the `ctrl_settings_ddr5` lighting data
returned by that service. If FURY CTRL changes that local protocol or reports a
different payload for a given product, NightLights logs the failure and skips
the module.

Supported operations:

- Save the current DIMM lighting as the day profile.
- Turn known DIMM slots off with the service's `all_off` lighting mode.
- Restore the saved day profile.
- Set a static day color with explicit brightness.

## MSI Mystic Light native module

The native MSI module uses MSI's published Mystic Light SDK:

- [MSI Mystic Light SDK download page](https://www.msi.com/Landing/mystic-light-rgb-gaming-pc/download)
- [MSI Mystic Light SDK developer guide](https://storage-asset.msi.com/file/pdf/Mystic_Light_Software_Development_Kit.pdf)

MSI describes the SDK as an LED-control API for MSI products such as
motherboards, graphics cards, keyboards, mice, and headsets. NightLights calls
only the subset it needs: initialize the SDK, enumerate device types and LED
counts, read zone colors, and set zone colors.

Supported operations:

- Save each reported LED zone's RGB color.
- Set reported LED zones to black at night.
- Restore saved zone colors.
- Set a static day color by scaling the RGB values to the requested brightness.

Limitations:

- `MysticLight_SDK.dll` is proprietary and is not bundled with NightLights.
- MSI Center or Mystic Light must be installed, and the SDK toggle must be on.
- NightLights does not manage Mystic Light effects, speed, or per-device vendor
  profiles. It only sets RGB colors for zones the SDK reports.

## Power saving and audio

The power and audio options are system features, not RGB hardware integrations.

Power saving uses Windows power-plan APIs to switch to the built-in Power saver
scheme at night, then restore the previous scheme when night mode ends. The
actual display timeout, sleep timeout, CPU policy, and energy savings come from
the Windows power plan and any device firmware or driver policies already on
the PC.

Audio muting uses the Windows Core Audio endpoint-volume API for the default
playback device. It does not control RGB lighting on speakers, headsets, or USB
audio devices unless those devices also appear through OpenRGB or MSI Mystic
Light.

## Troubleshooting support checks

1. Confirm the hardware works in its own control app first: FURY CTRL, MSI
   Center/Mystic Light, or OpenRGB.
2. In NightLights, open **Settings > Lighting modules** and enable only the
   module that should control the device.
3. For OpenRGB, start the SDK Server before testing. The default endpoint is
   `127.0.0.1:6742`.
4. Use **Test connection / list devices** and read the per-device status.
5. Check `%AppData%\NightLights\NightLights.log` when a module reports
   unavailable or unsupported hardware.

If the same RGB device is reachable through more than one module, enable only
one path for that hardware. For example, do not let both OpenRGB and Mystic
Light write to the same motherboard zones at the same time.
