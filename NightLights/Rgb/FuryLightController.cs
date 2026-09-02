using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using NightLights; // for AppSettings, Logger (parent namespace isn't visible automatically)

namespace NightLights.Rgb
{
    /// <summary>
    /// Talks directly to Kingston's FuryControllerService.exe over its local WebSocket API
    /// (ws://127.0.0.1:55599/) - the same background service the FURY CTRL GUI itself uses.
    /// FuryControllerService.exe already runs elevated/as-installed and already owns the
    /// SMBus driver, so this class needs no admin rights and never touches the hardware
    /// directly: it just sends it the same JSON commands the GUI would send, so the DIMM
    /// lighting keeps working exactly as Kingston intended even with FURY CTRL's window closed.
    ///
    /// Protocol details (endpoint, encryption, JSON shape) were recovered by decompiling
    /// FuryControllerService.exe for local interoperability - see FuryCrypto.cs for details
    /// and the accompanying README for how this was found.
    /// </summary>
    internal sealed class FuryLightController
    {
        private const string Endpoint = "ws://127.0.0.1:55599/";
        private const string CryptoKey = "3m23s45i599"; // the passphrase FuryControllerService itself uses
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

        private static readonly string CachePath =
            Path.Combine(AppSettings.AppDataFolder, "fury_led_snapshot.json");

        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        /// <summary>True if FuryControllerService answered on the local WebSocket just now.</summary>
        public async Task<bool> IsServiceRunningAsync()
        {
            try
            {
                string response = await SendCommandAsync("{\"root\":{\"api\":\"get_version\"}}").ConfigureAwait(false);
                return !string.IsNullOrEmpty(response);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Fetches the DIMMs' current lighting state and saves it to disk, so we have
        /// something to restore to later. Safe to call repeatedly - each call overwrites
        /// the cache with whatever is live right now, so if you change your lighting
        /// during the day, that becomes the new "restore to" state at the next sunset.
        /// </summary>
        public async Task<bool> RefreshSnapshotAsync()
        {
            try
            {
                string response = await SendCommandAsync("{\"root\":{\"api\":\"get_dram_led\"}}").ConfigureAwait(false);
                if (string.IsNullOrEmpty(response)) return false;

                var parsed = _json.DeserializeObject(response) as IDictionary<string, object>;
                var root = parsed?["root"] as IDictionary<string, object>;
                if (root == null) return false;

                if (!root.ContainsKey("ctrl_settings_ddr5") || !(root["ctrl_settings_ddr5"] is IDictionary<string, object> slots) || slots.Count == 0)
                {
                    Logger.Log("Fury: get_dram_led returned no ctrl_settings_ddr5 (no DDR5 DIMMs, or FURY CTRL profile empty).");
                    return false;
                }

                Directory.CreateDirectory(AppSettings.AppDataFolder);
                File.WriteAllText(CachePath, _json.Serialize(slots));
                Logger.Log($"Fury: snapshot saved ({slots.Count} slot(s)).");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Fury.RefreshSnapshotAsync failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Turns every known DIMM's lighting off ("all_off" mode).</summary>
        public async Task<bool> TurnOffAsync()
        {
            try
            {
                var slots = LoadSnapshot();
                if (slots == null)
                {
                    // No cache yet (first run) - snapshot now so we still know how many
                    // slots exist and what index each one is, then turn them off.
                    if (!await RefreshSnapshotAsync().ConfigureAwait(false)) return false;
                    slots = LoadSnapshot();
                    if (slots == null) return false;
                }

                var offSlots = new Dictionary<string, object>();
                foreach (var kv in slots)
                {
                    var slot = new Dictionary<string, object>();
                    if (kv.Value is IDictionary<string, object> src && src.ContainsKey("index"))
                    {
                        slot["index"] = src["index"]; // keep the DIMM address mapping intact
                    }
                    slot["mode"] = "all_off";
                    offSlots[kv.Key] = slot;
                }

                var request = new Dictionary<string, object>
                {
                    ["root"] = new Dictionary<string, object>
                    {
                        ["api"] = "set_dram_led",
                        ["ctrl_settings_ddr5"] = offSlots
                    }
                };

                string response = await SendCommandAsync(_json.Serialize(request)).ConfigureAwait(false);
                bool ok = ResponseOk(response);
                Logger.Log(ok ? "Fury: DIMM lighting turned off." : "Fury: turn-off request did not report success: " + response);
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Log("Fury.TurnOffAsync failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Restores the DIMM lighting to whatever was captured by the last snapshot.</summary>
        public async Task<bool> RestoreAsync()
        {
            try
            {
                var slots = LoadSnapshot();
                if (slots == null)
                {
                    Logger.Log("Fury: no snapshot to restore from (never captured a daytime state yet).");
                    return false;
                }

                var request = new Dictionary<string, object>
                {
                    ["root"] = new Dictionary<string, object>
                    {
                        ["api"] = "set_dram_led",
                        ["ctrl_settings_ddr5"] = slots
                    }
                };

                string response = await SendCommandAsync(_json.Serialize(request)).ConfigureAwait(false);
                bool ok = ResponseOk(response);
                Logger.Log(ok ? "Fury: DIMM lighting restored." : "Fury: restore request did not report success: " + response);
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Log("Fury.RestoreAsync failed: " + ex.Message);
                return false;
            }
        }

        private Dictionary<string, object> LoadSnapshot()
        {
            try
            {
                if (!File.Exists(CachePath)) return null;
                var obj = _json.DeserializeObject(File.ReadAllText(CachePath)) as IDictionary<string, object>;
                if (obj == null) return null;
                return new Dictionary<string, object>(obj);
            }
            catch (Exception ex)
            {
                Logger.Log("Fury.LoadSnapshot failed: " + ex.Message);
                return null;
            }
        }

        private bool ResponseOk(string response)
        {
            if (string.IsNullOrEmpty(response)) return false;
            var parsed = _json.DeserializeObject(response) as IDictionary<string, object>;
            var root = parsed?["root"] as IDictionary<string, object>;
            return root != null && root.ContainsKey("status") && Convert.ToString(root["status"]) == "0";
        }

        private async Task<string> SendCommandAsync(string plainJsonRequest)
        {
            using (var socket = new ClientWebSocket())
            using (var cts = new CancellationTokenSource(RequestTimeout))
            {
                await socket.ConnectAsync(new Uri(Endpoint), cts.Token).ConfigureAwait(false);

                string encrypted = FuryCrypto.Encrypt(plainJsonRequest, CryptoKey);
                byte[] outBytes = Encoding.UTF8.GetBytes(encrypted);
                await socket.SendAsync(new ArraySegment<byte>(outBytes), WebSocketMessageType.Text, true, cts.Token)
                    .ConfigureAwait(false);

                var buffer = new byte[1024 * 64];
                using (var ms = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    string encryptedResponse = Encoding.UTF8.GetString(ms.ToArray());
                    if (string.IsNullOrEmpty(encryptedResponse)) return null;

                    try
                    {
                        return FuryCrypto.Decrypt(encryptedResponse, CryptoKey);
                    }
                    finally
                    {
                        try
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch { /* best effort */ }
                    }
                }
            }
        }
    }
}
