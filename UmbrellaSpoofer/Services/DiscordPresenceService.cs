using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Timers;

namespace UmbrellaSpoofer.Services
{
    public class DiscordPresenceService : IDisposable
    {
        private NamedPipeClientStream? pipe;
        private string appId = "";
        private bool handshakeSent;
        private readonly Timer reconnectTimer = new Timer(5000);
        private string lastDetails = "Umbrella Spoofer";
        private string lastState = "Idle";
        private string lastStatus = "Not connected";

        public void Initialize(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId)) return;
            this.appId = appId;
            reconnectTimer.Elapsed += ReconnectTimer_Elapsed;
            reconnectTimer.AutoReset = true;
            reconnectTimer.Start();
            EnsureConnected();
            SetPresence(lastDetails, lastState);
        }

        public void SetPresence(string details, string state)
        {
            if (!EnsureConnected()) return;
            lastDetails = string.IsNullOrWhiteSpace(details) ? lastDetails : details;
            lastState = string.IsNullOrWhiteSpace(state) ? lastState : state;
            var payload = new Dictionary<string, object>
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = new Dictionary<string, object>
                {
                    ["pid"] = Process.GetCurrentProcess().Id,
                    ["activity"] = new Dictionary<string, object>
                    {
                            ["details"] = lastDetails,
                            ["state"] = lastState,
                        ["timestamps"] = new Dictionary<string, object>
                        {
                            ["start"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        }
                    }
                },
                ["nonce"] = Guid.NewGuid().ToString()
            };
            SendFrame(1, JsonSerializer.Serialize(payload));
        }

        public bool IsConnected => pipe?.IsConnected == true;
        public string Status => lastStatus;

        public void Dispose()
        {
            reconnectTimer.Stop();
            pipe?.Dispose();
        }

        bool EnsureConnected()
        {
            if (pipe != null && pipe.IsConnected) return true;
            pipe?.Dispose();
            pipe = ConnectPipe();
            handshakeSent = false;
            if (pipe == null || !pipe.IsConnected)
            {
                lastStatus = "Not connected";
                return false;
            }
            if (!handshakeSent)
            {
                SendFrame(0, JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["v"] = 1,
                    ["client_id"] = appId
                }));
                handshakeSent = true;
                lastStatus = "Connected";
            }
            return true;
        }

        NamedPipeClientStream? ConnectPipe()
        {
            for (var i = 0; i < 10; i++)
            {
                var name = $"discord-ipc-{i}";
                var p = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    p.Connect(200);
                    if (p.IsConnected) return p;
                }
                catch
                {
                    p.Dispose();
                }
            }
            return null;
        }

        void SendFrame(int op, string json)
        {
            if (pipe == null || !pipe.IsConnected) return;
            var data = Encoding.UTF8.GetBytes(json);
            var header = new byte[8];
            BitConverter.GetBytes(op).CopyTo(header, 0);
            BitConverter.GetBytes(data.Length).CopyTo(header, 4);
            pipe.Write(header, 0, header.Length);
            pipe.Write(data, 0, data.Length);
            pipe.Flush();
        }

        void ReconnectTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (EnsureConnected())
                SetPresence(lastDetails, lastState);
            else
                lastStatus = "Not connected";
        }
    }
}
