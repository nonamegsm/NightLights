using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NightLights.Rgb
{
    /// <summary>
    /// Optional OpenRGB SDK client for users who run the OpenRGB server.
    /// Uses protocol 3 deliberately: it preserves mode brightness while retaining
    /// the older fire-and-forget write behavior that OpenRGB supports broadly.
    /// </summary>
    internal sealed class OpenRgbController : ILightingModule
    {
        private const uint ProtocolVersion = 3;
        private const uint MaxPacketSize = 1024 * 1024;
        private const int RequestControllerCount = 0;
        private const int RequestControllerData = 1;
        private const int RequestProtocolVersion = 40;
        private const int SetClientName = 50;
        private const int DeviceListUpdated = 100;
        private const int UpdateLeds = 1050;
        private const int SetCustomMode = 1100;
        private const int UpdateMode = 1101;

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(4);
        private readonly string _host;
        private readonly int _port;
        private readonly string _snapshotPath;
        private readonly Func<IOpenRgbTransport> _transportFactory;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public OpenRgbController(string host, int port)
            : this(host, port, DefaultSnapshotPath(host, port), null)
        {
        }

        internal OpenRgbController(string host, int port, string snapshotPath, Func<IOpenRgbTransport> transportFactory)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("OpenRGB host is required.", nameof(host));
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), "OpenRGB port must be 1-65535.");

            _host = host.Trim();
            _port = port;
            _snapshotPath = snapshotPath;
            _transportFactory = transportFactory ?? (() => new TcpOpenRgbTransport(_host, _port, RequestTimeout));
        }

        public string Name => "OpenRGB";

        public async Task<string> ProbeAsync()
        {
            try
            {
                using (var session = await OpenSessionAsync().ConfigureAwait(false))
                {
                    var devices = await session.LoadDevicesAsync().ConfigureAwait(false);
                    if (devices.Count == 0) return "OpenRGB server answered, but reported no RGB controllers.";

                    int supported = devices.Count(d => d.SupportsStaticOrDirectControl);
                    string names = string.Join(", ", devices.Select(d => d.DisplayIdentity).Where(s => !string.IsNullOrWhiteSpace(s)));
                    return $"OpenRGB: {devices.Count} device(s), {supported} controllable for night lighting: {names}";
                }
            }
            catch (Exception ex)
            {
                Logger.Log("OpenRGB.ProbeAsync failed: " + ex.Message);
                return "OpenRGB unavailable: " + ex.Message;
            }
        }

        public async Task<bool> RefreshSnapshotAsync()
        {
            try
            {
                using (var session = await OpenSessionAsync().ConfigureAwait(false))
                {
                    var devices = await session.LoadDevicesAsync().ConfigureAwait(false);
                    if (devices.Count == 0)
                    {
                        Logger.Log("OpenRGB: no devices reported by the SDK server.");
                        return false;
                    }

                    WriteSnapshot(OpenRgbSnapshot.FromDevices(_host, _port, devices));
                    Logger.Log($"OpenRGB: snapshot saved ({devices.Count} device(s)).");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("OpenRGB.RefreshSnapshotAsync failed: " + ex.Message);
                return false;
            }
        }

        public async Task<bool> TurnOffAsync()
        {
            try
            {
                if (!File.Exists(_snapshotPath) && !await RefreshSnapshotAsync().ConfigureAwait(false))
                    return false;
                var snapshot = LoadSnapshot();
                if (snapshot == null)
                {
                    Logger.Log("OpenRGB: refusing to turn lights off because the saved day profile is missing, corrupt, or belongs to another endpoint.");
                    return false;
                }

                using (var session = await OpenSessionAsync().ConfigureAwait(false))
                {
                    var devices = await session.LoadDevicesAsync().ConfigureAwait(false);
                    if (MergeMissingSnapshotDevices(snapshot, devices))
                        WriteSnapshot(snapshot);

                    int relevant = devices.Count(d => d.HasAnyLitColor);
                    int changed = 0;
                    int unsupportedLit = 0;

                    foreach (var device in devices)
                    {
                        if (!device.SupportsStaticOrDirectControl)
                        {
                            if (device.HasAnyLitColor) unsupportedLit++;
                            Logger.Log("OpenRGB: skipping unsupported device " + device.DisplayIdentity + " (no direct/custom/static mode).");
                            continue;
                        }

                        if (await ApplyBlackAsync(session, device).ConfigureAwait(false))
                            changed++;
                    }

                    Logger.Log($"OpenRGB: turned off {changed}/{devices.Count} device(s).");
                    return changed > 0 && unsupportedLit == 0 && (relevant == 0 || changed >= relevant);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("OpenRGB.TurnOffAsync failed: " + ex.Message);
                return false;
            }
        }

        public async Task<bool> RestoreAsync()
        {
            try
            {
                var snapshot = LoadSnapshot();
                if (snapshot == null || snapshot.Devices == null || snapshot.Devices.Count == 0)
                {
                    Logger.Log("OpenRGB: no snapshot to restore from.");
                    return false;
                }

                using (var session = await OpenSessionAsync().ConfigureAwait(false))
                {
                    var current = await session.LoadDevicesAsync().ConfigureAwait(false);
                    int restored = 0;
                    int missing = 0;

                    var currentByKey = BuildUniqueDeviceMap(current);
                    foreach (var saved in snapshot.Devices)
                    {
                        if (!currentByKey.TryGetValue(saved.StableKey, out var device))
                        {
                            missing++;
                            Logger.Log("OpenRGB: saved device missing or ambiguous during restore: " + saved.DisplayIdentity);
                            continue;
                        }

                        if (await RestoreDeviceAsync(session, device, saved).ConfigureAwait(false))
                            restored++;
                    }

                    Logger.Log($"OpenRGB: restored {restored}/{snapshot.Devices.Count} device(s).");
                    return restored > 0 && missing == 0 && restored == snapshot.Devices.Count;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("OpenRGB.RestoreAsync failed: " + ex.Message);
                return false;
            }
        }

        public async Task<bool> SetStaticColorProfileAsync(byte r, byte g, byte b, int brightnessPercent)
        {
            try
            {
                using (var session = await OpenSessionAsync().ConfigureAwait(false))
                {
                    var devices = await session.LoadDevicesAsync().ConfigureAwait(false);
                    if (devices.Count == 0) return false;

                    int brightness = Clamp(brightnessPercent, 0, 100);
                    uint scaled = RgbColor(
                        (byte)Math.Round(r * brightness / 100.0),
                        (byte)Math.Round(g * brightness / 100.0),
                        (byte)Math.Round(b * brightness / 100.0));

                    var profileDevices = new List<OpenRgbSnapshotDevice>();
                    int unsupportedLit = 0;
                    foreach (var device in devices)
                    {
                        if (!device.SupportsStaticOrDirectControl)
                        {
                            if (device.HasAnyLitColor) unsupportedLit++;
                            Logger.Log("OpenRGB: skipping unsupported device for static profile: " + device.DisplayIdentity);
                            continue;
                        }

                        var colors = CreateRepeatedColor(device.Colors.Count, scaled);
                        var mode = FindStaticMode(device) ?? FindDirectMode(device);
                        var modeForProfile = mode?.Clone();
                        if (modeForProfile != null)
                        {
                            modeForProfile.Brightness = FullBrightness(modeForProfile);
                            modeForProfile.Colors = CreateRepeatedColor(Math.Max(1, modeForProfile.Colors.Count), scaled);
                        }
                        profileDevices.Add(OpenRgbSnapshotDevice.FromDevice(device, modeForProfile, colors));
                    }

                    if (profileDevices.Count == 0) return false;

                    WriteSnapshot(new OpenRgbSnapshot
                    {
                        Host = _host,
                        HostCanonical = CanonicalHost(_host),
                        Port = _port,
                        CreatedUtc = DateTime.UtcNow,
                        Devices = profileDevices
                    });
                    Logger.Log($"OpenRGB: day profile set to static color ({r},{g},{b}) at {brightness}% brightness.");
                    return unsupportedLit == 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("OpenRGB.SetStaticColorProfileAsync failed: " + ex.Message);
                return false;
            }
        }

        private async Task<OpenRgbSession> OpenSessionAsync()
        {
            var transport = _transportFactory();
            try
            {
                await transport.ConnectAsync().ConfigureAwait(false);
                var session = new OpenRgbSession(transport);
                await session.InitializeAsync().ConfigureAwait(false);
                return session;
            }
            catch
            {
                transport.Dispose();
                throw;
            }
        }

        private async Task<bool> ApplyBlackAsync(OpenRgbSession session, OpenRgbDevice device)
        {
            var black = CreateRepeatedColor(device.Colors.Count, 0);
            var mode = FindDirectMode(device) ?? FindStaticMode(device);
            bool wroteMode = false;
            if (mode != null)
            {
                var clone = mode.Clone();
                clone.Brightness = FullBrightness(clone);
                clone.Colors = NormalizeColors(black, Math.Max(1, clone.Colors.Count));
                wroteMode = await session.SendModeAsync(device, clone, device.Modes.IndexOf(mode)).ConfigureAwait(false);
            }

            bool customOrDirect = FindDirectMode(device) != null;
            if (customOrDirect)
                await session.SendAsync(device.Id, SetCustomMode, new byte[0]).ConfigureAwait(false);

            bool wroteLeds = await session.SendColorsAsync(device, black).ConfigureAwait(false);
            var refreshed = await session.ReadDeviceAsync(device.Id).ConfigureAwait(false);
            return (wroteMode || customOrDirect) && wroteLeds && !refreshed.HasAnyLitColor;
        }

        private async Task<bool> RestoreDeviceAsync(OpenRgbSession session, OpenRgbDevice device, OpenRgbSnapshotDevice saved)
        {
            bool wroteColors = false;
            int modeIndex = -1;

            if (saved.Colors != null && saved.Colors.Count > 0)
            {
                if (FindDirectMode(device) != null)
                    await session.SendAsync(device.Id, SetCustomMode, new byte[0]).ConfigureAwait(false);
                wroteColors = await session.SendColorsAsync(device, saved.Colors).ConfigureAwait(false);
            }

            bool wroteMode = false;
            if (saved.Mode != null)
            {
                modeIndex = FindCompatibleModeIndex(device, saved.ActiveModeName, saved.ActiveModeValue);
                if (modeIndex >= 0)
                    wroteMode = await session.SendModeAsync(device, saved.Mode.Clone(), modeIndex).ConfigureAwait(false);
            }

            var refreshed = await session.ReadDeviceAsync(device.Id).ConfigureAwait(false);
            return (wroteColors || wroteMode) &&
                   ColorsMatch(saved.Colors, refreshed.Colors) &&
                   ModeMatches(saved, refreshed, modeIndex);
        }

        private static bool ModeMatches(OpenRgbSnapshotDevice expected, OpenRgbDevice actual, int expectedModeIndex)
        {
            if (expected.Mode == null) return true;
            if (expectedModeIndex < 0 || actual.ActiveMode != expectedModeIndex || expectedModeIndex >= actual.Modes.Count) return false;

            var mode = actual.Modes[expectedModeIndex];
            return string.Equals(mode.Name, expected.Mode.Name, StringComparison.OrdinalIgnoreCase) &&
                   mode.Value == expected.Mode.Value &&
                   mode.Flags == expected.Mode.Flags &&
                   mode.SpeedMin == expected.Mode.SpeedMin &&
                   mode.SpeedMax == expected.Mode.SpeedMax &&
                   mode.BrightnessMin == expected.Mode.BrightnessMin &&
                   mode.BrightnessMax == expected.Mode.BrightnessMax &&
                   mode.ColorsMin == expected.Mode.ColorsMin &&
                   mode.ColorsMax == expected.Mode.ColorsMax &&
                   mode.Speed == expected.Mode.Speed &&
                   mode.Brightness == expected.Mode.Brightness &&
                   mode.Direction == expected.Mode.Direction &&
                   mode.ColorMode == expected.Mode.ColorMode &&
                   (expected.Mode.Colors ?? new List<uint>()).SequenceEqual(mode.Colors ?? new List<uint>());
        }

        private static bool ColorsMatch(List<uint> expected, List<uint> actual)
        {
            if (expected == null || expected.Count == 0) return true;
            var normalized = NormalizeColors(expected, actual.Count);
            return normalized.SequenceEqual(actual);
        }

        private static bool MergeMissingSnapshotDevices(OpenRgbSnapshot snapshot, IEnumerable<OpenRgbDevice> devices)
        {
            var existing = new HashSet<string>((snapshot.Devices ?? new List<OpenRgbSnapshotDevice>()).Select(d => d.StableKey));
            var uniqueDevices = BuildUniqueDeviceMap(devices).Values;
            bool changed = false;
            foreach (var device in uniqueDevices)
            {
                if (existing.Contains(device.StableKey)) continue;
                snapshot.Devices.Add(OpenRgbSnapshotDevice.FromDevice(device, null, device.Colors));
                existing.Add(device.StableKey);
                changed = true;
            }
            return changed;
        }

        private void WriteSnapshot(OpenRgbSnapshot snapshot)
        {
            EnsureSnapshotDirectory();
            string tmp = _snapshotPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, _json.Serialize(snapshot));
            if (File.Exists(_snapshotPath))
            {
                File.Replace(tmp, _snapshotPath, null);
            }
            else
            {
                File.Move(tmp, _snapshotPath);
            }
        }

        private OpenRgbSnapshot LoadSnapshot()
        {
            try
            {
                if (!File.Exists(_snapshotPath)) return null;
                var snapshot = _json.Deserialize<OpenRgbSnapshot>(File.ReadAllText(_snapshotPath));
                if (!IsValidSnapshot(snapshot)) return null;
                return snapshot;
            }
            catch (Exception ex)
            {
                Logger.Log("OpenRGB.LoadSnapshot failed: " + ex.Message);
                return null;
            }
        }

        private bool IsValidSnapshot(OpenRgbSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Devices == null || snapshot.Devices.Count == 0) return false;
            if (!string.Equals(snapshot.HostCanonical, CanonicalHost(_host), StringComparison.Ordinal)) return false;
            if (snapshot.Port != _port) return false;
            return snapshot.Devices.All(d => !string.IsNullOrEmpty(d.StableKey)) &&
                   snapshot.Devices.GroupBy(d => d.StableKey).All(g => g.Count() == 1);
        }

        private static Dictionary<string, OpenRgbDevice> BuildUniqueDeviceMap(IEnumerable<OpenRgbDevice> devices)
        {
            return devices
                .GroupBy(d => d.StableKey)
                .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.First());
        }

        private void EnsureSnapshotDirectory()
        {
            string directory = Path.GetDirectoryName(_snapshotPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }

        private static OpenRgbDevice ParseDevice(uint id, byte[] payload)
        {
            var reader = new PacketReader(payload, MaxPacketSize);
            uint declared = reader.ReadUInt32();
            if (declared != payload.Length) throw new InvalidDataException("OpenRGB controller-data packet size mismatch.");

            var device = new OpenRgbDevice { Id = id };
            device.Type = reader.ReadUInt32();
            device.Name = reader.ReadString();
            device.Vendor = reader.ReadString();
            device.Description = reader.ReadString();
            device.Version = reader.ReadString();
            device.Serial = reader.ReadString();
            device.Location = reader.ReadString();

            ushort modeCount = reader.ReadUInt16();
            device.ActiveMode = reader.ReadInt32();
            for (int i = 0; i < modeCount; i++)
                device.Modes.Add(ParseMode(reader));

            ushort zoneCount = reader.ReadUInt16();
            for (int i = 0; i < zoneCount; i++)
                SkipZone(reader);

            ushort ledCount = reader.ReadUInt16();
            for (int i = 0; i < ledCount; i++)
            {
                device.Leds.Add(new OpenRgbLed
                {
                    Name = reader.ReadString(),
                    Value = reader.ReadUInt32()
                });
            }

            ushort colorCount = reader.ReadUInt16();
            for (int i = 0; i < colorCount; i++)
                device.Colors.Add(reader.ReadUInt32());

            if (!reader.End) throw new InvalidDataException("OpenRGB controller-data packet had trailing data.");
            return device;
        }

        private static OpenRgbMode ParseMode(PacketReader reader)
        {
            var mode = new OpenRgbMode
            {
                Name = reader.ReadString(),
                Value = reader.ReadInt32(),
                Flags = reader.ReadUInt32(),
                SpeedMin = reader.ReadUInt32(),
                SpeedMax = reader.ReadUInt32(),
                BrightnessMin = reader.ReadUInt32(),
                BrightnessMax = reader.ReadUInt32(),
                ColorsMin = reader.ReadUInt32(),
                ColorsMax = reader.ReadUInt32(),
                Speed = reader.ReadUInt32(),
                Brightness = reader.ReadUInt32(),
                Direction = reader.ReadUInt32(),
                ColorMode = reader.ReadUInt32()
            };

            ushort colors = reader.ReadUInt16();
            for (int i = 0; i < colors; i++)
                mode.Colors.Add(reader.ReadUInt32());
            return mode;
        }

        private static byte[] SerializeMode(OpenRgbMode mode)
        {
            return PacketWriter.Build(w =>
            {
                w.WriteString(mode.Name);
                w.WriteInt32(mode.Value);
                w.WriteUInt32(mode.Flags);
                w.WriteUInt32(mode.SpeedMin);
                w.WriteUInt32(mode.SpeedMax);
                w.WriteUInt32(mode.BrightnessMin);
                w.WriteUInt32(mode.BrightnessMax);
                w.WriteUInt32(mode.ColorsMin);
                w.WriteUInt32(mode.ColorsMax);
                w.WriteUInt32(mode.Speed);
                w.WriteUInt32(mode.Brightness);
                w.WriteUInt32(mode.Direction);
                w.WriteUInt32(mode.ColorMode);
                w.WriteUInt16((ushort)Math.Min(ushort.MaxValue, mode.Colors.Count));
                foreach (uint color in mode.Colors.Take(ushort.MaxValue)) w.WriteUInt32(color);
            });
        }

        private static void SkipZone(PacketReader reader)
        {
            reader.ReadString();
            reader.ReadUInt32();
            reader.ReadUInt32();
            reader.ReadUInt32();
            reader.ReadUInt32();
            ushort matrixLength = reader.ReadUInt16();
            if (matrixLength == 0) return;

            uint height = reader.ReadUInt32();
            uint width = reader.ReadUInt32();
            ulong cells = (ulong)height * width;
            if (cells > ushort.MaxValue) throw new InvalidDataException("OpenRGB matrix map is too large.");
            for (ulong i = 0; i < cells; i++) reader.ReadUInt32();
        }

        private static OpenRgbMode FindStaticMode(OpenRgbDevice device)
        {
            return device.Modes.FirstOrDefault(m =>
                string.Equals(m.Name, "static", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Name, "static color", StringComparison.OrdinalIgnoreCase) ||
                m.Name.IndexOf("static", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static OpenRgbMode FindDirectMode(OpenRgbDevice device)
        {
            return device.Modes.FirstOrDefault(m =>
                string.Equals(m.Name, "direct", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Name, "custom", StringComparison.OrdinalIgnoreCase) ||
                m.Name.IndexOf("direct", StringComparison.OrdinalIgnoreCase) >= 0 ||
                m.Name.IndexOf("custom", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static int FindCompatibleModeIndex(OpenRgbDevice device, string name, int value)
        {
            if (!string.IsNullOrEmpty(name))
            {
                int byName = device.Modes.FindIndex(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
                if (byName >= 0) return byName;
            }

            int byValue = device.Modes.FindIndex(m => m.Value == value);
            if (byValue >= 0) return byValue;

            var fallback = FindStaticMode(device) ?? FindDirectMode(device);
            return fallback == null ? -1 : device.Modes.IndexOf(fallback);
        }

        private static List<uint> NormalizeColors(List<uint> colors, int targetCount)
        {
            int count = Math.Max(1, targetCount);
            var result = new List<uint>(count);
            uint fallback = colors != null && colors.Count > 0 ? colors[0] : 0;
            for (int i = 0; i < count; i++)
                result.Add(colors != null && i < colors.Count ? colors[i] : fallback);
            return result;
        }

        private static List<uint> CreateRepeatedColor(int count, uint color)
        {
            return Enumerable.Repeat(color, Math.Max(1, count)).ToList();
        }

        private static uint FullBrightness(OpenRgbMode mode)
        {
            return Math.Max(mode.BrightnessMin, mode.BrightnessMax);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static uint RgbColor(byte r, byte g, byte b)
        {
            return (uint)(r | (g << 8) | (b << 16));
        }

        private static string DefaultSnapshotPath(string host, int port)
        {
            string canonical = CanonicalHost(host);
            string visibleHost = new string(canonical.Select(c => char.IsLetterOrDigit(c) ? c : '_').Take(40).ToArray());
            return Path.Combine(AppSettings.AppDataFolder, $"openrgb_{visibleHost}_{port}_{ShortHash(canonical)}_snapshot.json");
        }

        private static string CanonicalHost(string host)
        {
            return (host ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string ShortHash(string value)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return BitConverter.ToString(hash, 0, 6).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        internal sealed class OpenRgbSession : IDisposable
        {
            private readonly IOpenRgbTransport _transport;

            public OpenRgbSession(IOpenRgbTransport transport)
            {
                _transport = transport;
            }

            public async Task InitializeAsync()
            {
                var request = PacketWriter.Build(w => w.WriteUInt32(ProtocolVersion));
                var response = await SendAndReadExpectedAsync(0, RequestProtocolVersion, request).ConfigureAwait(false);
                if (response.Payload.Length == 4)
                {
                    uint server = new PacketReader(response.Payload, MaxPacketSize).ReadUInt32();
                    if (server < ProtocolVersion)
                        throw new NotSupportedException("OpenRGB SDK server is older than protocol 3; brightness-safe mode restore is unavailable.");
                }
                else
                {
                    throw new InvalidDataException("OpenRGB protocol-version reply had an invalid length.");
                }

                await SendAsync(0, SetClientName, Encoding.ASCII.GetBytes("NightLights\0")).ConfigureAwait(false);
            }

            public async Task<List<OpenRgbDevice>> LoadDevicesAsync()
            {
                var countPacket = await SendAndReadExpectedAsync(0, RequestControllerCount, new byte[0]).ConfigureAwait(false);
                var reader = new PacketReader(countPacket.Payload, MaxPacketSize);
                uint count = reader.ReadUInt32();
                if (!reader.End) throw new InvalidDataException("OpenRGB controller-count packet had trailing data.");
                if (count > 256) throw new InvalidDataException("OpenRGB reported an implausible controller count.");

                var devices = new List<OpenRgbDevice>();
                for (uint i = 0; i < count; i++)
                    devices.Add(await ReadDeviceAsync(i).ConfigureAwait(false));
                return devices;
            }

            public async Task<OpenRgbDevice> ReadDeviceAsync(uint id)
            {
                var body = PacketWriter.Build(w => w.WriteUInt32(ProtocolVersion));
                var packet = await SendAndReadExpectedAsync(id, RequestControllerData, body).ConfigureAwait(false);
                return ParseDevice(id, packet.Payload);
            }

            private async Task<OpenRgbPacket> SendAndReadExpectedAsync(uint deviceId, int packetId, byte[] payload)
            {
                var packet = await _transport.SendAndReadAsync(deviceId, packetId, payload).ConfigureAwait(false);
                if (packet.PacketId == DeviceListUpdated)
                    throw new InvalidDataException("OpenRGB device list changed during the operation; retry on the next poll.");
                if (packet.PacketId != packetId)
                    throw new InvalidDataException("OpenRGB reply packet id did not match the request.");
                if (packet.DeviceId != deviceId && packetId == RequestControllerData)
                    throw new InvalidDataException("OpenRGB reply device id did not match the request.");
                return packet;
            }

            public Task SendAsync(uint deviceId, int packetId, byte[] payload)
            {
                return _transport.SendAsync(deviceId, packetId, payload);
            }

            public Task<bool> SendColorsAsync(OpenRgbDevice device, List<uint> colors)
            {
                var normalized = NormalizeColors(colors, device.Colors.Count);
                byte[] payload = PacketWriter.Build(w =>
                {
                    w.WriteUInt32((uint)(4 + 2 + normalized.Count * 4));
                    w.WriteUInt16((ushort)normalized.Count);
                    foreach (uint color in normalized) w.WriteUInt32(color);
                });

                return SendBooleanAsync(device.Id, UpdateLeds, payload);
            }

            public Task<bool> SendModeAsync(OpenRgbDevice device, OpenRgbMode mode, int modeIndex)
            {
                if (modeIndex < 0) return Task.FromResult(false);
                byte[] payload = PacketWriter.Build(w =>
                {
                    byte[] modeBytes = SerializeMode(mode);
                    w.WriteUInt32((uint)(4 + 4 + modeBytes.Length));
                    w.WriteInt32(modeIndex);
                    w.WriteBytes(modeBytes);
                });

                return SendBooleanAsync(device.Id, UpdateMode, payload);
            }

            private async Task<bool> SendBooleanAsync(uint deviceId, int packetId, byte[] payload)
            {
                await _transport.SendAsync(deviceId, packetId, payload).ConfigureAwait(false);
                return true;
            }

            public void Dispose()
            {
                _transport.Dispose();
            }
        }

        internal sealed class OpenRgbDevice
        {
            public uint Id { get; set; }
            public uint Type { get; set; }
            public string Name { get; set; }
            public string Vendor { get; set; }
            public string Description { get; set; }
            public string Version { get; set; }
            public string Serial { get; set; }
            public string Location { get; set; }
            public int ActiveMode { get; set; }
            public List<OpenRgbMode> Modes { get; set; } = new List<OpenRgbMode>();
            public List<OpenRgbLed> Leds { get; set; } = new List<OpenRgbLed>();
            public List<uint> Colors { get; set; } = new List<uint>();
            public string StableKey => string.Join("|", new[] { Vendor, Name, Serial, Location }.Select(s => s ?? string.Empty));
            public string DisplayIdentity => string.IsNullOrEmpty(Vendor) ? Name : Vendor + " " + Name;
            public bool SupportsStaticOrDirectControl => FindStaticMode(this) != null || FindDirectMode(this) != null;
            public bool HasAnyLitColor => Colors != null && Colors.Any(c => c != 0);
        }

        public sealed class OpenRgbMode
        {
            public string Name { get; set; }
            public int Value { get; set; }
            public uint Flags { get; set; }
            public uint SpeedMin { get; set; }
            public uint SpeedMax { get; set; }
            public uint BrightnessMin { get; set; }
            public uint BrightnessMax { get; set; }
            public uint ColorsMin { get; set; }
            public uint ColorsMax { get; set; }
            public uint Speed { get; set; }
            public uint Brightness { get; set; }
            public uint Direction { get; set; }
            public uint ColorMode { get; set; }
            public List<uint> Colors { get; set; } = new List<uint>();

            public OpenRgbMode Clone()
            {
                return new OpenRgbMode
                {
                    Name = Name,
                    Value = Value,
                    Flags = Flags,
                    SpeedMin = SpeedMin,
                    SpeedMax = SpeedMax,
                    BrightnessMin = BrightnessMin,
                    BrightnessMax = BrightnessMax,
                    ColorsMin = ColorsMin,
                    ColorsMax = ColorsMax,
                    Speed = Speed,
                    Brightness = Brightness,
                    Direction = Direction,
                    ColorMode = ColorMode,
                    Colors = new List<uint>(Colors ?? new List<uint>())
                };
            }
        }

        internal sealed class OpenRgbLed
        {
            public string Name { get; set; }
            public uint Value { get; set; }
        }

        public sealed class OpenRgbSnapshot
        {
            public string Host { get; set; }
            public string HostCanonical { get; set; }
            public int Port { get; set; }
            public DateTime CreatedUtc { get; set; }
            public List<OpenRgbSnapshotDevice> Devices { get; set; }

            internal static OpenRgbSnapshot FromDevices(string host, int port, IEnumerable<OpenRgbDevice> devices)
            {
                return new OpenRgbSnapshot
                {
                    Host = host,
                    HostCanonical = CanonicalHost(host),
                    Port = port,
                    CreatedUtc = DateTime.UtcNow,
                    Devices = devices.Select(d => OpenRgbSnapshotDevice.FromDevice(d, null, d.Colors)).ToList()
                };
            }
        }

        public sealed class OpenRgbSnapshotDevice
        {
            public string StableKey { get; set; }
            public string DisplayIdentity { get; set; }
            public string ActiveModeName { get; set; }
            public int ActiveModeValue { get; set; }
            public OpenRgbMode Mode { get; set; }
            public List<uint> Colors { get; set; }

            internal static OpenRgbSnapshotDevice FromDevice(OpenRgbDevice device, OpenRgbMode overrideMode, List<uint> colors)
            {
                var active = overrideMode;
                if (active == null && device.ActiveMode >= 0 && device.ActiveMode < device.Modes.Count)
                    active = device.Modes[device.ActiveMode].Clone();

                return new OpenRgbSnapshotDevice
                {
                    StableKey = device.StableKey,
                    DisplayIdentity = device.DisplayIdentity,
                    ActiveModeName = active?.Name,
                    ActiveModeValue = active?.Value ?? -1,
                    Mode = active,
                    Colors = new List<uint>(colors ?? new List<uint>())
                };
            }
        }

        internal interface IOpenRgbTransport : IDisposable
        {
            Task ConnectAsync();
            Task SendAsync(uint deviceId, int packetId, byte[] payload);
            Task<OpenRgbPacket> SendAndReadAsync(uint deviceId, int packetId, byte[] payload);
        }

        internal sealed class OpenRgbPacket
        {
            public uint DeviceId { get; set; }
            public int PacketId { get; set; }
            public byte[] Payload { get; set; }
        }

        internal sealed class TcpOpenRgbTransport : IOpenRgbTransport
        {
            private readonly string _host;
            private readonly int _port;
            private readonly TimeSpan _timeout;
            private TcpClient _client;
            private NetworkStream _stream;

            public TcpOpenRgbTransport(string host, int port, TimeSpan timeout)
            {
                _host = host;
                _port = port;
                _timeout = timeout;
            }

            public async Task ConnectAsync()
            {
                _client = new TcpClient();
                var connect = _client.ConnectAsync(_host, _port);
                if (await Task.WhenAny(connect, Task.Delay(_timeout)).ConfigureAwait(false) != connect)
                    throw new TimeoutException("Timed out connecting to OpenRGB.");
                await connect.ConfigureAwait(false);
                _client.ReceiveTimeout = (int)_timeout.TotalMilliseconds;
                _client.SendTimeout = (int)_timeout.TotalMilliseconds;
                _stream = _client.GetStream();
            }

            public Task SendAsync(uint deviceId, int packetId, byte[] payload)
            {
                return WritePacketAsync(deviceId, packetId, payload);
            }

            public async Task<OpenRgbPacket> SendAndReadAsync(uint deviceId, int packetId, byte[] payload)
            {
                await WritePacketAsync(deviceId, packetId, payload).ConfigureAwait(false);
                return await ReadPacketAsync().ConfigureAwait(false);
            }

            public void Dispose()
            {
                _stream?.Dispose();
                _client?.Close();
            }

            private async Task WritePacketAsync(uint deviceId, int packetId, byte[] payload)
            {
                byte[] header = PacketWriter.Build(w =>
                {
                    w.WriteBytes(Encoding.ASCII.GetBytes("ORGB"));
                    w.WriteUInt32(deviceId);
                    w.WriteUInt32((uint)packetId);
                    w.WriteUInt32((uint)(payload?.Length ?? 0));
                });

                await WithTimeout(_stream.WriteAsync(header, 0, header.Length)).ConfigureAwait(false);
                if (payload != null && payload.Length > 0)
                    await WithTimeout(_stream.WriteAsync(payload, 0, payload.Length)).ConfigureAwait(false);
            }

            private async Task<OpenRgbPacket> ReadPacketAsync()
            {
                DateTime deadlineUtc = DateTime.UtcNow.Add(_timeout);
                byte[] header = await ReadExactAsync(16, deadlineUtc).ConfigureAwait(false);
                if (Encoding.ASCII.GetString(header, 0, 4) != "ORGB")
                    throw new InvalidDataException("OpenRGB packet magic mismatch.");

                uint size = BitConverter.ToUInt32(header, 12);
                if (size > MaxPacketSize) throw new InvalidDataException("OpenRGB packet exceeded the NightLights size limit.");

                return new OpenRgbPacket
                {
                    DeviceId = BitConverter.ToUInt32(header, 4),
                    PacketId = unchecked((int)BitConverter.ToUInt32(header, 8)),
                    Payload = size == 0 ? new byte[0] : await ReadExactAsync((int)size, deadlineUtc).ConfigureAwait(false)
                };
            }

            private async Task<byte[]> ReadExactAsync(int size, DateTime deadlineUtc)
            {
                byte[] buffer = new byte[size];
                int offset = 0;
                while (offset < size)
                {
                    int read = await WithTimeout(_stream.ReadAsync(buffer, offset, size - offset), deadlineUtc).ConfigureAwait(false);
                    if (read <= 0) throw new IOException("OpenRGB socket closed unexpectedly.");
                    offset += read;
                }
                return buffer;
            }

            private async Task WithTimeout(Task task)
            {
                if (await Task.WhenAny(task, Task.Delay(_timeout)).ConfigureAwait(false) != task)
                    throw new TimeoutException("Timed out talking to OpenRGB.");
                await task.ConfigureAwait(false);
            }

            private async Task<int> WithTimeout(Task<int> task)
            {
                if (await Task.WhenAny(task, Task.Delay(_timeout)).ConfigureAwait(false) != task)
                    throw new TimeoutException("Timed out talking to OpenRGB.");
                return await task.ConfigureAwait(false);
            }

            private async Task<int> WithTimeout(Task<int> task, DateTime deadlineUtc)
            {
                TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    throw new TimeoutException("Timed out talking to OpenRGB.");
                if (await Task.WhenAny(task, Task.Delay(remaining)).ConfigureAwait(false) != task)
                    throw new TimeoutException("Timed out talking to OpenRGB.");
                return await task.ConfigureAwait(false);
            }
        }

        internal sealed class PacketReader
        {
            private readonly byte[] _data;
            private readonly uint _maxSize;
            private int _offset;

            public PacketReader(byte[] data, uint maxSize)
            {
                _data = data ?? new byte[0];
                _maxSize = maxSize;
                if (_data.Length > _maxSize) throw new InvalidDataException("OpenRGB payload exceeds configured maximum.");
            }

            public bool End => _offset == _data.Length;

            public ushort ReadUInt16()
            {
                Ensure(2);
                ushort value = BitConverter.ToUInt16(_data, _offset);
                _offset += 2;
                return value;
            }

            public uint ReadUInt32()
            {
                Ensure(4);
                uint value = BitConverter.ToUInt32(_data, _offset);
                _offset += 4;
                return value;
            }

            public int ReadInt32()
            {
                Ensure(4);
                int value = BitConverter.ToInt32(_data, _offset);
                _offset += 4;
                return value;
            }

            public string ReadString()
            {
                ushort len = ReadUInt16();
                if (len == 0) return string.Empty;
                Ensure(len);
                string value = Encoding.UTF8.GetString(_data, _offset, len);
                _offset += len;
                return value.TrimEnd('\0');
            }

            private void Ensure(int count)
            {
                if (count < 0 || _offset + count > _data.Length)
                    throw new InvalidDataException("Malformed OpenRGB packet.");
            }
        }

        internal sealed class PacketWriter
        {
            private readonly MemoryStream _stream = new MemoryStream();

            public static byte[] Build(Action<PacketWriter> write)
            {
                var writer = new PacketWriter();
                write(writer);
                return writer._stream.ToArray();
            }

            public void WriteBytes(byte[] bytes)
            {
                _stream.Write(bytes, 0, bytes.Length);
            }

            public void WriteUInt16(ushort value)
            {
                WriteBytes(BitConverter.GetBytes(value));
            }

            public void WriteUInt32(uint value)
            {
                WriteBytes(BitConverter.GetBytes(value));
            }

            public void WriteInt32(int value)
            {
                WriteBytes(BitConverter.GetBytes(value));
            }

            public void WriteString(string value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes((value ?? string.Empty) + "\0");
                if (bytes.Length > ushort.MaxValue) throw new InvalidDataException("OpenRGB string field is too long.");
                WriteUInt16((ushort)bytes.Length);
                WriteBytes(bytes);
            }
        }
    }
}
