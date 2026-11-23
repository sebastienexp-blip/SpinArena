using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpinArena.Core.Live;
using UnityEngine;

namespace SpinArena.Live
{
    /// <summary>
    /// Connecteur réel : reçoit les événements du live depuis le pont Termux (bridge/spin-bridge.mjs) par WebSocket local.
    /// Le pont utilise la bibliothèque non officielle TikTok-Live-Connector ; TikTok ne propose pas d'API publique
    /// des cadeaux pour ce type d'usage. Reconnexion automatique ; aucun événement n'est inventé.
    /// </summary>
    public sealed class BridgeLiveConnector : ILiveConnector
    {
        [Serializable] sealed class Dto
        {
            public string type, msgId, comment, giftId, giftName, groupId, giftImageUrl, status, detail;
            public int diamondCount, repeatCount, likeCount;
            public bool repeatEnd, streakable;
            public LiveUser user;
        }

        readonly string _url;
        readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
        CancellationTokenSource _cts;
        volatile bool _connected;
        volatile string _status = "arrêté";
        string _tiktokState = "";
        public int Received { get; private set; }
        public int Errors { get; private set; }

        public BridgeLiveConnector(string url) { _url = url; }

        public string Name => "Pont TikTok (Termux)";
        public bool IsDemo => false;
        public bool IsConnected => _connected;
        public string StatusText => _connected ? ("pont connecté" + (string.IsNullOrEmpty(_tiktokState) ? "" : " · TikTok : " + _tiktokState)) : _status;
        public event Action<LiveEvent> EventReceived;

        public void Connect()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            Task.Run(() => Loop(token));
        }

        public void Disconnect()
        {
            _cts?.Cancel();
            _cts = null;
            _connected = false;
            _status = "arrêté";
        }

        async Task Loop(CancellationToken token)
        {
            int delayMs = 1000;
            var buffer = new byte[64 * 1024];
            while (!token.IsCancellationRequested)
            {
                using (var ws = new ClientWebSocket())
                {
                    try
                    {
                        _status = "connexion au pont…";
                        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                        await ws.ConnectAsync(new Uri(_url), token);
                        _connected = true;
                        delayMs = 1000;
                        var sb = new StringBuilder();
                        while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                        {
                            var res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                            if (res.MessageType == WebSocketMessageType.Close) break;
                            sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
                            if (res.EndOfMessage)
                            {
                                _inbox.Enqueue(sb.ToString());
                                sb.Clear();
                            }
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception e)
                    {
                        Errors++;
                        _status = "pont injoignable (lance « spin-bridge » dans Termux) — " + e.GetType().Name;
                    }
                    finally { _connected = false; }
                }
                try { await Task.Delay(delayMs, token); } catch (OperationCanceledException) { break; }
                delayMs = Math.Min(delayMs * 2, 8000);
            }
        }

        public void Pump(double now)
        {
            int budget = 300; // au plus 300 messages par image : le jeu reste fluide même en cas d'avalanche
            while (budget-- > 0 && _inbox.TryDequeue(out var json))
            {
                Dto d;
                try { d = JsonUtility.FromJson<Dto>(json); }
                catch (Exception) { Errors++; continue; }
                if (d == null || string.IsNullOrEmpty(d.type)) continue;
                var e = new LiveEvent
                {
                    msgId = d.msgId, user = d.user, comment = d.comment, giftId = d.giftId, giftName = d.giftName,
                    diamondCount = d.diamondCount, repeatCount = d.repeatCount, repeatEnd = d.repeatEnd, streakable = d.streakable,
                    groupId = d.groupId, giftImageUrl = d.giftImageUrl, likeCount = d.likeCount, status = d.status, receivedAt = now
                };
                switch (d.type)
                {
                    case "chat": e.type = LiveEventType.Chat; break;
                    case "gift": e.type = LiveEventType.Gift; break;
                    case "like": e.type = LiveEventType.Like; break;
                    case "member": e.type = LiveEventType.Member; break;
                    case "follow": e.type = LiveEventType.Follow; break;
                    case "share": e.type = LiveEventType.Share; break;
                    case "status":
                        e.type = LiveEventType.Status;
                        _tiktokState = d.status + (string.IsNullOrEmpty(d.detail) ? "" : " (" + d.detail + ")");
                        break;
                    default: continue;
                }
                if (e.type != LiveEventType.Status && (e.user == null || string.IsNullOrEmpty(e.user.Key))) continue;
                Received++;
                try { EventReceived?.Invoke(e); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }
    }
}
