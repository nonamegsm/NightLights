using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NightLights.Rgb;

namespace NightLights.Tests
{
    internal static class OpenRgbTests
    {
        public static void Run()
        {
            ProbeListsDeviceNames().GetAwaiter().GetResult();
            TurnOffUsesProtocol3FireAndForgetWrites().GetAwaiter().GetResult();
            TurnOffRefusesCorruptSnapshotWithoutWrites().GetAwaiter().GetResult();
            TurnOffMergesOnlyNewHotPluggedDevicesIntoSnapshot().GetAwaiter().GetResult();
            RestorePreservesExactModeAndPerLedColorsAfterReorder().GetAwaiter().GetResult();
            StatefulRestoreLeavesSavedModeActive().GetAwaiter().GetResult();
            RestoreSkipsAmbiguousDuplicateIdentities().GetAwaiter().GetResult();
            ProbeRejectsMalformedProtocolReplies().GetAwaiter().GetResult();
            ProbeRejectsTopologyUpdateAsReply().GetAwaiter().GetResult();
            StaticProfileScalesColorButUsesFullModeBrightness().GetAwaiter().GetResult();
            CapabilityBasedModesBlackOutAndRestore().GetAwaiter().GetResult();
            MixedDeviceReportAndPartialControlAreAccurate().GetAwaiter().GetResult();
            MissingIdentityCannotCreateRestoreBaseline().GetAwaiter().GetResult();
            LoopbackProtocol3NoAckAndFragmentedReplies().GetAwaiter().GetResult();
            TestAssert.Throws<InvalidDataException>(() => new OpenRgbController.PacketReader(new byte[] { 1 }, 1024).ReadUInt32(), "short packet is rejected");
        }

        private static async Task ProbeListsDeviceNames()
        {
            var script = new FakeScript();
            script.EnqueueDeviceList(new[] { SampleDevice("Board", "MSI", "serial-a", 0x00010203u) });
            var controller = new OpenRgbController("127.0.0.1", 6742, TempSnapshotPath(), script.CreateTransport);

            string result = await controller.ProbeAsync().ConfigureAwait(false);

            TestAssert.True(result.Contains("1 device(s), 1 controllable"), "probe reports controllable count: " + result);
            TestAssert.True(result.Contains("MSI Board"), "probe includes device name: " + result);
        }

        private static async Task TurnOffUsesProtocol3FireAndForgetWrites()
        {
            string snapshotPath = TempSnapshotPath();

            var script = new FakeScript();
            script.EnqueueDeviceList(new[] { SampleDevice("Board", "MSI", "serial-a", 0x00010203u) });
            script.EnqueueTurnOff(new[] { SampleDevice("Board", "MSI", "serial-a", 0x00010203u) });
            var controller = new OpenRgbController("127.0.0.1", 6742, snapshotPath, script.CreateTransport);

            TestAssert.True(await controller.RefreshSnapshotAsync().ConfigureAwait(false), "seed snapshot");
            bool ok = await controller.TurnOffAsync().ConfigureAwait(false);

            TestAssert.True(ok, "turn off succeeds without ACKs");
            var rgbWrites = script.Writes.Where(w => w.PacketId == 1101 || w.PacketId == 1100 || w.PacketId == 1050).ToList();
            TestAssert.Equal(1050, rgbWrites[0].PacketId, "LED buffer is updated before applying the color mode");
            TestAssert.Equal(1101, rgbWrites[1].PacketId, "explicit mode is selected last without relying on SetCustomMode");
            TestAssert.Equal(2, rgbWrites.Count, "only the LED and explicit mode writes are needed");

            var reader = new OpenRgbController.PacketReader(rgbWrites[0].Payload, 1024);
            TestAssert.Equal(14u, reader.ReadUInt32(), "LED update size prefix");
            TestAssert.Equal((ushort)2, reader.ReadUInt16(), "LED color count");
            TestAssert.Equal(0u, reader.ReadUInt32(), "first LED off color");
            TestAssert.Equal(0u, reader.ReadUInt32(), "second LED off color");
        }

        private static async Task TurnOffRefusesCorruptSnapshotWithoutWrites()
        {
            string snapshotPath = TempSnapshotPath();
            File.WriteAllText(snapshotPath, "{bad json");
            var script = new FakeScript();
            var controller = new OpenRgbController("127.0.0.1", 6742, snapshotPath, script.CreateTransport);

            TestAssert.True(!await controller.TurnOffAsync().ConfigureAwait(false), "corrupt snapshot blocks turn off");
            TestAssert.Equal(0, script.Writes.Count, "no OpenRGB writes happen with corrupt snapshot");
        }

        private static async Task TurnOffMergesOnlyNewHotPluggedDevicesIntoSnapshot()
        {
            string snapshotPath = TempSnapshotPath();
            var oldDay = SampleDevice("Board", "MSI", "serial-a", 0x00010203u);
            var oldCurrentlyBlack = SampleDevice("Board", "MSI", "serial-a", 0);
            var newDevice = SampleDevice("Keyboard", "Corsair", "serial-b", 0x00040506u);

            var script = new FakeScript();
            script.EnqueueDeviceList(new[] { oldDay });
            script.EnqueueTurnOff(new[] { oldCurrentlyBlack, newDevice });
            var controller = new OpenRgbController("127.0.0.1", 6742, snapshotPath, script.CreateTransport);

            TestAssert.True(await controller.RefreshSnapshotAsync().ConfigureAwait(false), "initial baseline saved");
            TestAssert.True(await controller.TurnOffAsync().ConfigureAwait(false), "turn off handles new device");

            string json = File.ReadAllText(snapshotPath);
            TestAssert.True(json.Contains("66051"), "old baseline color is preserved");
            TestAssert.True(json.Contains("263430"), "new hot-plugged device baseline is merged");
        }

        private static async Task RestorePreservesExactModeAndPerLedColorsAfterReorder()
        {
            string snapshotPath = TempSnapshotPath();
            var initial = new[]
            {
                SampleDevice("Board", "MSI", "serial-a", 0x00010203u),
                SampleDevice("Keyboard", "Corsair", "serial-b", 0x00040506u)
            };
            initial[0].Modes[0].Speed = 77;
            initial[0].Modes[0].Direction = 3;
            initial[0].Modes[0].ColorMode = 2;
            initial[0].Modes[0].Brightness = 42;

            var reordered = new[]
            {
                SampleDevice("Keyboard", "Corsair", "serial-b", 0),
                SampleDevice("Board", "MSI", "serial-a", 0)
            };

            var script = new FakeScript();
            script.EnqueueDeviceList(initial);
            script.EnqueueRestore(reordered, initial);

            var controller = new OpenRgbController("127.0.0.1", 6742, snapshotPath, script.CreateTransport);
            TestAssert.True(await controller.RefreshSnapshotAsync().ConfigureAwait(false), "initial snapshot saved");
            TestAssert.True(await controller.RestoreAsync().ConfigureAwait(false), "restore succeeds after device reorder");

            var updateModeWrites = script.Writes.Where(w => w.PacketId == 1101).ToList();
            TestAssert.Equal(2, updateModeWrites.Count, "restore sends one mode update per saved device");
            TestAssert.True(updateModeWrites.Any(w => w.DeviceId == 0 && PayloadContainsColor(w.Payload, 0x00040506u)), "keyboard mode color restored to reordered id 0");
            TestAssert.True(updateModeWrites.Any(w => w.DeviceId == 1 && PayloadContainsColor(w.Payload, 0x00010203u)), "board mode color restored to reordered id 1");
            TestAssert.True(updateModeWrites.Any(w => w.DeviceId == 1 && PayloadContainsUInt32(w.Payload, 77) && PayloadContainsUInt32(w.Payload, 42)), "mode speed and raw brightness are preserved");

            var ledWrites = script.Writes.Where(w => w.PacketId == 1050).ToList();
            TestAssert.True(ledWrites.Any(w => w.DeviceId == 0 && PayloadContainsColor(w.Payload, 0x00040506u)), "keyboard per-LED colors restored");
            TestAssert.True(ledWrites.Any(w => w.DeviceId == 1 && PayloadContainsColor(w.Payload, 0x00010203u)), "board per-LED colors restored");
        }

        private static async Task StaticProfileScalesColorButUsesFullModeBrightness()
        {
            string snapshotPath = TempSnapshotPath();
            var script = new FakeScript();
            script.EnqueueDeviceList(new[] { SampleDevice("Board", "MSI", "serial-a", 0) });
            var controller = new OpenRgbController("127.0.0.1", 6742, snapshotPath, script.CreateTransport);

            TestAssert.True(await controller.SetStaticColorProfileAsync(100, 50, 20, 50).ConfigureAwait(false), "static profile succeeds");

            string json = File.ReadAllText(snapshotPath);
            TestAssert.True(json.Contains("661810"), "snapshot contains scaled packed RGB value");
            TestAssert.True(json.Contains("\"Brightness\":100"), "snapshot uses full mode brightness after scaling color");
        }

        private static async Task CapabilityBasedModesBlackOutAndRestore()
        {
            foreach (uint flags in new[] { 1u << 5, 1u << 6 })
            {
                var device = SampleDevice("Unusual controller", "Example", "new-mode", 0x00010203u);
                device.Modes.RemoveAt(1);
                device.Modes[0].Name = "Vendor configurable effect";
                device.Modes[0].Flags = flags | (1u << 7);
                device.Modes[0].ColorMode = 3;
                device.Modes[0].ColorsMin = 2;
                device.Modes[0].ColorsMax = 4;
                var transport = new StatefulOpenRgbTransport(new[] { device });
                string path = TempSnapshotPath();
                var controller = new OpenRgbController("127.0.0.1", 6742, path, () => transport);
                try
                {
                    TestAssert.True(await controller.RefreshSnapshotAsync(), "nonstandard mode baseline saved");
                    TestAssert.True(await controller.TurnOffAsync(), "SDK color capabilities allow blackout with nonstandard names");
                    TestAssert.True(device.Colors.All(c => c == 0), "all LED colors black");
                    TestAssert.Equal(flags == (1u << 5) ? 1u : 2u, device.Modes[0].ColorMode, "random color selection explicitly disabled");
                    if (flags == (1u << 6)) TestAssert.Equal(2, device.Modes[0].Colors.Count, "hardware's palette minimum honored");
                    TestAssert.True(await controller.RestoreAsync(), "original nonstandard mode restored");
                    TestAssert.Equal(3u, device.Modes[0].ColorMode, "original effect settings preserved for day");
                    TestAssert.True(device.Colors.All(c => c == 0x00010203u), "original colors restored");
                    TestAssert.True(await controller.SetStaticColorProfileAsync(100, 50, 20, 50), "day color available through SDK capability");
                    TestAssert.True(await controller.RestoreAsync(), "chosen day color can be applied");
                    TestAssert.Equal(661810u, device.Colors[0], "brightness is applied once");
                    TestAssert.Equal(100u, device.Modes[0].Brightness, "mode brightness remains full after color scaling");
                }
                finally { File.Delete(path); }
            }
        }

        private static async Task MixedDeviceReportAndPartialControlAreAccurate()
        {
            var good = SampleDevice("Keyboard", "Example", "good", 0x00010203u);
            var bad = SampleDevice("Effects only", "Example", "bad", 0);
            bad.Modes = new List<SampleOpenRgbMode> { new SampleOpenRgbMode { Name = "Rainbow", Flags = 1u << 7, ColorMode = 3, Colors = new List<uint> { 0x00ffffff } } };
            var transport = new StatefulOpenRgbTransport(new[] { good, bad });
            string path = TempSnapshotPath();
            var controller = new OpenRgbController("127.0.0.1", 6742, path, () => transport);
            try
            {
                string report = await controller.ProbeAsync();
                TestAssert.True(report.Contains("2 device(s), 1 controllable"), "mixed server reports actual compatible count");
                TestAssert.True(report.Contains("Effects only") && report.Contains("Unavailable:"), "unsupported device is individually explained");
                TestAssert.True(await controller.RefreshSnapshotAsync(), "mixed server baseline saved");
                TestAssert.True(!await controller.TurnOffAsync(), "black cached buffers don't imply random hardware effects are off");
                TestAssert.True(good.Colors.All(c => c == 0), "compatible sibling still turned off");
                TestAssert.True(transport.Writes.Where(w => w.PacketId >= 1000).All(w => w.DeviceId == 0), "incompatible device receives no hardware writes");
                TestAssert.True(!await controller.SetStaticColorProfileAsync(100, 50, 20, 50), "partially configured server reports partial result");
                TestAssert.True(File.ReadAllText(path).Contains("Effects only"), "color profile updates preserve unsupported-device baseline");
            }
            finally { File.Delete(path); }
        }

        private static async Task MissingIdentityCannotCreateRestoreBaseline()
        {
            var device = SampleDevice(null, "Vendor only", null, 1);
            device.Location = null;
            var transport = new StatefulOpenRgbTransport(new[] { device });
            string path = TempSnapshotPath();
            var controller = new OpenRgbController("127.0.0.1", 6742, path, () => transport);
            TestAssert.True(!await controller.RefreshSnapshotAsync(), "anonymous hardware has no restorable identity");
            TestAssert.True(!await controller.TurnOffAsync(), "anonymous hardware is not blacked out");
            TestAssert.True(!File.Exists(path), "anonymous device cannot create a restore file");
            TestAssert.True(transport.Writes.All(w => w.PacketId < 1000), "identity failure causes no RGB writes");
        }

        private static async Task RestoreSkipsAmbiguousDuplicateIdentities()
        {
            string snapshotPath = TempSnapshotPath();
            var saved = SampleDevice("Board", "MSI", "serial-a", 0x00010203u);
            var duplicateA = SampleDevice("Board", "MSI", "serial-a", 0);
            var duplicateB = SampleDevice("Board", "MSI", "serial-a", 0);

            var script = new FakeScript();
            script.EnqueueDeviceList(new[] { saved });
            script.EnqueueDeviceList(new[] { duplicateA, duplicateB });
            var controller = new OpenRgbController("127.0.0.1", 6742, snapshotPath, script.CreateTransport);

            TestAssert.True(await controller.RefreshSnapshotAsync().ConfigureAwait(false), "baseline saved for duplicate test");
            TestAssert.True(!await controller.RestoreAsync().ConfigureAwait(false), "ambiguous duplicate identities are not restored");
            TestAssert.True(!script.Writes.Any(w => w.PacketId == 1050 || w.PacketId == 1101), "ambiguous devices receive no lighting writes");
        }

        private static async Task ProbeRejectsMalformedProtocolReplies()
        {
            var script = new FakeScript();
            script.EnqueueMalformedProtocolVersion();
            var controller = new OpenRgbController("127.0.0.1", 6742, TempSnapshotPath(), script.CreateTransport);

            string result = await controller.ProbeAsync().ConfigureAwait(false);
            TestAssert.True(result.Contains("invalid length"), "malformed version reply is reported");
        }

        private static async Task ProbeRejectsTopologyUpdateAsReply()
        {
            var script = new FakeScript();
            script.EnqueueTopologyUpdateInsteadOfCount();
            var controller = new OpenRgbController("127.0.0.1", 6742, TempSnapshotPath(), script.CreateTransport);

            string result = await controller.ProbeAsync().ConfigureAwait(false);
            TestAssert.True(result.Contains("device list changed"), "topology update packet is not parsed as data");
        }

        private static async Task StatefulRestoreLeavesSavedModeActive()
        {
            string snapshotPath = TempSnapshotPath();
            var device = SampleDevice("Board", "MSI", "serial-a", 0x00010203u);
            device.Modes[0].Speed = 77;
            device.Modes[0].Direction = 3;
            device.Modes[0].ColorMode = 2;
            device.Modes[0].Brightness = 42;

            var transport = new StatefulOpenRgbTransport(new[] { device });
            var controller = new OpenRgbController("127.0.0.1", 6742, snapshotPath, () => transport);
            TestAssert.True(await controller.RefreshSnapshotAsync().ConfigureAwait(false), "stateful snapshot saved");

            transport.Devices[0].ActiveModeIndex = 1;
            transport.Devices[0].Colors = new List<uint> { 0, 0 };
            transport.Devices[0].Modes[0].Speed = 1;
            transport.Devices[0].Modes[0].Brightness = 1;

            TestAssert.True(await controller.RestoreAsync().ConfigureAwait(false), "stateful restore succeeds");
            TestAssert.Equal(0, transport.Devices[0].ActiveModeIndex, "saved static mode is final active mode");
            TestAssert.Equal(77u, transport.Devices[0].Modes[0].Speed, "saved mode speed is restored");
            TestAssert.Equal(42u, transport.Devices[0].Modes[0].Brightness, "saved raw brightness is restored");
            TestAssert.True(transport.Devices[0].Colors.SequenceEqual(new[] { 0x00010203u, 0x00010203u }), "saved per-LED buffer is restored");
            TestAssert.Equal(1101, transport.Writes.Last(w => w.PacketId == 1100 || w.PacketId == 1050 || w.PacketId == 1101).PacketId, "saved mode write is last");
        }

        private static async Task LoopbackProtocol3NoAckAndFragmentedReplies()
        {
            string snapshotPath = TempSnapshotPath();

            var server = new LoopbackOpenRgbServer(new[] { SampleDevice("Board", "MSI", "serial-a", 0x00010203u) });
            int port = server.Start();
            var controller = new OpenRgbController("127.0.0.1", port, snapshotPath, null);

            TestAssert.True(await controller.RefreshSnapshotAsync().ConfigureAwait(false), "loopback seed snapshot");
            TestAssert.True(await controller.TurnOffAsync().ConfigureAwait(false), "real TCP protocol 3 turn off completes without ACKs");
            await server.Done.ConfigureAwait(false);
            TestAssert.True(server.Writes.Any(w => w.PacketId == 1050), "loopback server saw LED update write");
            TestAssert.True(server.Writes.All(w => w.PacketId != 10), "client never expected or sent ACK packets");
        }

        private static bool PayloadContainsColor(byte[] payload, uint color)
        {
            return PayloadContainsUInt32(payload, color);
        }

        private static bool PayloadContainsUInt32(byte[] payload, uint value)
        {
            for (int i = 0; i <= payload.Length - 4; i++)
            {
                if (BitConverter.ToUInt32(payload, i) == value) return true;
            }
            return false;
        }

        private static SampleOpenRgbDevice SampleDevice(string name, string vendor, string serial, uint color)
        {
            return new SampleOpenRgbDevice
            {
                Name = name,
                Vendor = vendor,
                Serial = serial,
                Location = "USB-" + serial,
                Colors = new List<uint> { color, color },
                Modes = new List<SampleOpenRgbMode>
                {
                    new SampleOpenRgbMode { Name = "Static", Value = 1, Speed = 10, Direction = 1, ColorMode = 1, Brightness = 50, BrightnessMin = 0, BrightnessMax = 100, Colors = new List<uint> { color } },
                    new SampleOpenRgbMode { Name = "Direct", Value = 2, Speed = 20, Direction = 2, ColorMode = 1, Brightness = 100, BrightnessMin = 0, BrightnessMax = 100, Colors = new List<uint> { color } }
                }
            };
        }

        private static string TempSnapshotPath()
        {
            string dir = Path.Combine(Path.GetTempPath(), "NightLights.OpenRgbTests");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json");
        }

        private sealed class FakeScript
        {
            private readonly Queue<FakeOpenRgbTransport> _transports = new Queue<FakeOpenRgbTransport>();
            public readonly List<FakeWrite> Writes = new List<FakeWrite>();

            public OpenRgbController.IOpenRgbTransport CreateTransport()
            {
                return _transports.Dequeue();
            }

            public void EnqueueDeviceList(IEnumerable<SampleOpenRgbDevice> devices)
            {
                var transport = NewTransport();
                transport.EnqueueDeviceList(devices.ToList());
                _transports.Enqueue(transport);
            }

            public void EnqueueTurnOff(IEnumerable<SampleOpenRgbDevice> devices)
            {
                var list = devices.ToList();
                var off = list.Select(d => d.WithColors(0)).ToList();
                foreach (var device in off) device.ActiveModeIndex = 1;
                var transport = NewTransport();
                transport.EnqueueDeviceList(list);
                for (int i = 0; i < list.Count; i++)
                {
                    transport.ExpectWrite(1050);
                    transport.ExpectWrite(1101);
                    transport.EnqueueDevice((uint)i, off[i]);
                }
                _transports.Enqueue(transport);
            }

            public void EnqueueRestore(IList<SampleOpenRgbDevice> current, IList<SampleOpenRgbDevice> savedOrder)
            {
                var transport = NewTransport();
                transport.EnqueueDeviceList(current.ToList());
                foreach (var saved in savedOrder)
                {
                    int currentIndex = current.ToList().FindIndex(d => d.Serial == saved.Serial);
                    TestAssert.True(currentIndex >= 0, "saved device exists in current fake list");
                    transport.ExpectWrite(1100);
                    transport.ExpectWrite(1050);
                    transport.ExpectWrite(1101);
                    transport.EnqueueDevice((uint)currentIndex, saved);
                }
                _transports.Enqueue(transport);
            }

            public void EnqueueMalformedProtocolVersion()
            {
                var transport = new FakeOpenRgbTransport(Writes);
                transport.Respond(40, Body(w => w.WriteUInt16(6)));
                _transports.Enqueue(transport);
            }

            public void EnqueueTopologyUpdateInsteadOfCount()
            {
                var transport = NewTransport();
                transport.RespondPacket(0, 100, new byte[0]);
                _transports.Enqueue(transport);
            }

            private FakeOpenRgbTransport NewTransport()
            {
                var transport = new FakeOpenRgbTransport(Writes);
                transport.Respond(40, Body(w => w.WriteUInt32(6)));
                transport.ExpectWrite(50);
                return transport;
            }
        }

        private sealed class FakeOpenRgbTransport : OpenRgbController.IOpenRgbTransport
        {
            private readonly Queue<Func<uint, int, OpenRgbController.OpenRgbPacket>> _responses = new Queue<Func<uint, int, OpenRgbController.OpenRgbPacket>>();
            private readonly List<FakeWrite> _writes;

            public FakeOpenRgbTransport(List<FakeWrite> writes)
            {
                _writes = writes;
            }

            public Task ConnectAsync()
            {
                return Task.FromResult(0);
            }

            public Task SendAsync(uint deviceId, int packetId, byte[] payload)
            {
                _writes.Add(new FakeWrite { DeviceId = deviceId, PacketId = packetId, Payload = payload ?? new byte[0] });
                _responses.Dequeue()(deviceId, packetId);
                return Task.FromResult(0);
            }

            public Task<OpenRgbController.OpenRgbPacket> SendAndReadAsync(uint deviceId, int packetId, byte[] payload)
            {
                _writes.Add(new FakeWrite { DeviceId = deviceId, PacketId = packetId, Payload = payload ?? new byte[0] });
                return Task.FromResult(_responses.Dequeue()(deviceId, packetId));
            }

            public void Dispose()
            {
            }

            public void EnqueueDeviceList(IList<SampleOpenRgbDevice> devices)
            {
                Respond(0, Body(w => w.WriteUInt32((uint)devices.Count)));
                for (int i = 0; i < devices.Count; i++)
                    EnqueueDevice((uint)i, devices[i]);
            }

            public void EnqueueDevice(uint id, SampleOpenRgbDevice device)
            {
                Respond(1, BuildDevicePayload(device));
            }

            public void Respond(int expectedPacketId, byte[] payload)
            {
                _responses.Enqueue((deviceId, packetId) =>
                {
                    TestAssert.Equal(expectedPacketId, packetId, "fake transport request order");
                    return new OpenRgbController.OpenRgbPacket { DeviceId = deviceId, PacketId = packetId, Payload = payload };
                });
            }

            public void RespondPacket(int expectedPacketId, int responsePacketId, byte[] payload)
            {
                _responses.Enqueue((deviceId, packetId) =>
                {
                    TestAssert.Equal(expectedPacketId, packetId, "fake transport request order");
                    return new OpenRgbController.OpenRgbPacket { DeviceId = deviceId, PacketId = responsePacketId, Payload = payload };
                });
            }

            public void ExpectWrite(int expectedPacketId)
            {
                _responses.Enqueue((deviceId, packetId) =>
                {
                    TestAssert.Equal(expectedPacketId, packetId, "fake transport write order");
                    return new OpenRgbController.OpenRgbPacket { DeviceId = deviceId, PacketId = packetId, Payload = new byte[0] };
                });
            }
        }

        private sealed class LoopbackOpenRgbServer
        {
            private readonly List<SampleOpenRgbDevice> _devices;
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>();
            public readonly List<FakeWrite> Writes = new List<FakeWrite>();

            public LoopbackOpenRgbServer(IEnumerable<SampleOpenRgbDevice> devices)
            {
                _devices = devices.ToList();
            }

            public Task Done => _done.Task;

            public int Start()
            {
                _listener.Start();
                Task.Run(() => RunAsync());
                return ((IPEndPoint)_listener.LocalEndpoint).Port;
            }

            private async Task RunAsync()
            {
                try
                {
                    int readDataRequests = 0;
                    while (readDataRequests < 3)
                    {
                        using (var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false))
                        using (var stream = client.GetStream())
                        {
                            while (readDataRequests < 3)
                            {
                                FakeWrite request;
                                try
                                {
                                    request = await ReadPacketAsync(stream).ConfigureAwait(false);
                                }
                                catch (IOException)
                                {
                                    break;
                                }

                                Writes.Add(request);
                                if (request.PacketId == 40)
                                    await WritePacketFragmentedAsync(stream, 0, 40, Body(w => w.WriteUInt32(6))).ConfigureAwait(false);
                                else if (request.PacketId == 0)
                                    await WritePacketFragmentedAsync(stream, 0, 0, Body(w => w.WriteUInt32((uint)_devices.Count))).ConfigureAwait(false);
                                else if (request.PacketId == 1)
                                {
                                    var device = _devices[(int)request.DeviceId];
                                    await WritePacketFragmentedAsync(stream, request.DeviceId, 1, BuildDevicePayload(device)).ConfigureAwait(false);
                                    readDataRequests++;
                                }
                                else if (request.PacketId == 1050)
                                {
                                    var reader = new OpenRgbController.PacketReader(request.Payload, 1024);
                                    reader.ReadUInt32();
                                    int count = reader.ReadUInt16();
                                    _devices[(int)request.DeviceId].Colors = Enumerable.Range(0, count).Select(_ => reader.ReadUInt32()).ToList();
                                }
                                else if (request.PacketId == 1101)
                                {
                                    var reader = new OpenRgbController.PacketReader(request.Payload, 1024);
                                    reader.ReadUInt32();
                                    int mode = reader.ReadInt32();
                                    _devices[(int)request.DeviceId].ActiveModeIndex = mode;
                                    _devices[(int)request.DeviceId].Modes[mode] = ReadSampleMode(reader);
                                }
                            }
                        }
                    }
                    _done.SetResult(true);
                }
                catch (Exception ex)
                {
                    _done.SetException(ex);
                }
                finally
                {
                    _listener.Stop();
                }
            }

            private static async Task<FakeWrite> ReadPacketAsync(NetworkStream stream)
            {
                byte[] header = await ReadExactAsync(stream, 16).ConfigureAwait(false);
                int size = unchecked((int)BitConverter.ToUInt32(header, 12));
                byte[] payload = size == 0 ? new byte[0] : await ReadExactAsync(stream, size).ConfigureAwait(false);
                return new FakeWrite
                {
                    DeviceId = BitConverter.ToUInt32(header, 4),
                    PacketId = unchecked((int)BitConverter.ToUInt32(header, 8)),
                    Payload = payload
                };
            }

            private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int size)
            {
                byte[] buffer = new byte[size];
                int offset = 0;
                while (offset < size)
                {
                    int read = await stream.ReadAsync(buffer, offset, size - offset).ConfigureAwait(false);
                    if (read <= 0) throw new IOException("socket closed");
                    offset += read;
                }
                return buffer;
            }

            private static async Task WritePacketFragmentedAsync(NetworkStream stream, uint deviceId, int packetId, byte[] payload)
            {
                byte[] header = Body(w =>
                {
                    w.WriteBytes(System.Text.Encoding.ASCII.GetBytes("ORGB"));
                    w.WriteUInt32(deviceId);
                    w.WriteUInt32((uint)packetId);
                    w.WriteUInt32((uint)payload.Length);
                });

                await stream.WriteAsync(header, 0, 5).ConfigureAwait(false);
                await stream.WriteAsync(header, 5, header.Length - 5).ConfigureAwait(false);
                if (payload.Length > 0)
                {
                    int first = Math.Min(3, payload.Length);
                    await stream.WriteAsync(payload, 0, first).ConfigureAwait(false);
                    await stream.WriteAsync(payload, first, payload.Length - first).ConfigureAwait(false);
                }
            }
        }

        private sealed class StatefulOpenRgbTransport : OpenRgbController.IOpenRgbTransport
        {
            public readonly List<SampleOpenRgbDevice> Devices;
            public readonly List<FakeWrite> Writes = new List<FakeWrite>();

            public StatefulOpenRgbTransport(IEnumerable<SampleOpenRgbDevice> devices)
            {
                Devices = devices.ToList();
            }

            public Task ConnectAsync()
            {
                return Task.FromResult(0);
            }

            public Task SendAsync(uint deviceId, int packetId, byte[] payload)
            {
                Writes.Add(new FakeWrite { DeviceId = deviceId, PacketId = packetId, Payload = payload ?? new byte[0] });
                if (packetId == 1050)
                {
                    var reader = new OpenRgbController.PacketReader(payload, 1024);
                    reader.ReadUInt32();
                    ushort count = reader.ReadUInt16();
                    var colors = new List<uint>();
                    for (int i = 0; i < count; i++) colors.Add(reader.ReadUInt32());
                    Devices[(int)deviceId].Colors = colors;
                }
                else if (packetId == 1101)
                {
                    var reader = new OpenRgbController.PacketReader(payload, 1024);
                    reader.ReadUInt32();
                    int modeIndex = reader.ReadInt32();
                    Devices[(int)deviceId].ActiveModeIndex = modeIndex;
                    Devices[(int)deviceId].Modes[modeIndex] = ReadSampleMode(reader);
                }
                else if (packetId == 1100)
                {
                    Devices[(int)deviceId].ActiveModeIndex = Math.Max(0, Devices[(int)deviceId].Modes.FindIndex(m => m.Name == "Direct"));
                }
                return Task.FromResult(0);
            }

            public Task<OpenRgbController.OpenRgbPacket> SendAndReadAsync(uint deviceId, int packetId, byte[] payload)
            {
                Writes.Add(new FakeWrite { DeviceId = deviceId, PacketId = packetId, Payload = payload ?? new byte[0] });
                byte[] response;
                if (packetId == 40) response = Body(w => w.WriteUInt32(6));
                else if (packetId == 0) response = Body(w => w.WriteUInt32((uint)Devices.Count));
                else response = BuildDevicePayload(Devices[(int)deviceId]);
                return Task.FromResult(new OpenRgbController.OpenRgbPacket { DeviceId = deviceId, PacketId = packetId, Payload = response });
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeWrite
        {
            public uint DeviceId { get; set; }
            public int PacketId { get; set; }
            public byte[] Payload { get; set; }
        }

        private sealed class SampleOpenRgbDevice
        {
            public string Name;
            public string Vendor;
            public string Serial;
            public string Location;
            public int ActiveModeIndex;
            public List<uint> Colors;
            public List<SampleOpenRgbMode> Modes;

            public SampleOpenRgbDevice WithColors(uint color)
            {
                return new SampleOpenRgbDevice
                {
                    Name = Name,
                    Vendor = Vendor,
                    Serial = Serial,
                    Location = Location,
                    ActiveModeIndex = ActiveModeIndex,
                    Colors = Colors.Select(_ => color).ToList(),
                    Modes = Modes.Select(m => m.WithColors(color)).ToList()
                };
            }
        }

        private sealed class SampleOpenRgbMode
        {
            public string Name;
            public int Value;
            public uint Flags;
            public uint ColorsMin = 1;
            public uint ColorsMax = 16;
            public uint Speed;
            public uint Direction;
            public uint ColorMode;
            public uint Brightness;
            public uint BrightnessMin;
            public uint BrightnessMax;
            public List<uint> Colors;

            public SampleOpenRgbMode WithColors(uint color)
            {
                return new SampleOpenRgbMode
                {
                    Name = Name,
                    Value = Value,
                    Flags = Flags,
                    ColorsMin = ColorsMin,
                    ColorsMax = ColorsMax,
                    Speed = Speed,
                    Direction = Direction,
                    ColorMode = ColorMode,
                    Brightness = Brightness,
                    BrightnessMin = BrightnessMin,
                    BrightnessMax = BrightnessMax,
                    Colors = Colors.Select(_ => color).ToList()
                };
            }
        }

        private static byte[] BuildDevicePayload(SampleOpenRgbDevice device)
        {
            byte[] body = Body(w =>
            {
                w.WriteUInt32(0);
                w.WriteUInt32(0);
                w.WriteString(device.Name);
                w.WriteString(device.Vendor);
                w.WriteString("Test controller");
                w.WriteString("1.0");
                w.WriteString(device.Serial);
                w.WriteString(device.Location);
                w.WriteUInt16((ushort)device.Modes.Count);
                w.WriteInt32(device.ActiveModeIndex);
                foreach (var mode in device.Modes)
                    WriteMode(w, mode);
                w.WriteUInt16(0);
                w.WriteUInt16((ushort)device.Colors.Count);
                for (int i = 0; i < device.Colors.Count; i++)
                {
                    w.WriteString("LED " + i);
                    w.WriteUInt32((uint)i);
                }
                w.WriteUInt16((ushort)device.Colors.Count);
                foreach (uint color in device.Colors) w.WriteUInt32(color);
            });

            Array.Copy(BitConverter.GetBytes((uint)body.Length), body, 4);
            return body;
        }

        private static void WriteMode(OpenRgbController.PacketWriter w, SampleOpenRgbMode mode)
        {
            w.WriteString(mode.Name);
            w.WriteInt32(mode.Value);
            w.WriteUInt32(mode.Flags);
            w.WriteUInt32(0);
            w.WriteUInt32(100);
            w.WriteUInt32(mode.BrightnessMin);
            w.WriteUInt32(mode.BrightnessMax);
            w.WriteUInt32(mode.ColorsMin);
            w.WriteUInt32(mode.ColorsMax);
            w.WriteUInt32(mode.Speed);
            w.WriteUInt32(mode.Brightness);
            w.WriteUInt32(mode.Direction);
            w.WriteUInt32(mode.ColorMode);
            w.WriteUInt16((ushort)mode.Colors.Count);
            foreach (uint color in mode.Colors) w.WriteUInt32(color);
        }

        private static SampleOpenRgbMode ReadSampleMode(OpenRgbController.PacketReader reader)
        {
            var mode = new SampleOpenRgbMode();
            mode.Name = reader.ReadString();
            mode.Value = reader.ReadInt32();
            mode.Flags = reader.ReadUInt32();
            reader.ReadUInt32();
            reader.ReadUInt32();
            mode.BrightnessMin = reader.ReadUInt32();
            mode.BrightnessMax = reader.ReadUInt32();
            mode.ColorsMin = reader.ReadUInt32();
            mode.ColorsMax = reader.ReadUInt32();
            mode.Speed = reader.ReadUInt32();
            mode.Brightness = reader.ReadUInt32();
            mode.Direction = reader.ReadUInt32();
            mode.ColorMode = reader.ReadUInt32();
            ushort count = reader.ReadUInt16();
            mode.Colors = new List<uint>();
            for (int i = 0; i < count; i++) mode.Colors.Add(reader.ReadUInt32());
            return mode;
        }

        private static byte[] Body(Action<OpenRgbController.PacketWriter> write)
        {
            return OpenRgbController.PacketWriter.Build(write);
        }
    }
}
