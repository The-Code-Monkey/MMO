using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MMO.Social
{
    /// <summary>
    /// The production <see cref="ISocketTransport"/>: a <see cref="ClientWebSocket"/> wrapper. All
    /// of its work happens on background threads; the <see cref="NakamaSocialSocket"/> only touches
    /// the results on the main thread by way of a concurrent queue.
    /// </summary>
    public sealed class WebSocketTransport : ISocketTransport
    {
        private const int ReceiveBufferSize = 8192;

        private ClientWebSocket _socket;
        private readonly byte[] _buffer = new byte[ReceiveBufferSize];

        public bool IsOpen => _socket != null && _socket.State == WebSocketState.Open;

        /// <summary>
        /// Connects, replacing any previous socket. A <see cref="ClientWebSocket"/> cannot be
        /// reopened once it has closed or faulted, so every (re)connect needs a fresh instance —
        /// which is exactly what the socket's 1 → 30 s backoff loop relies on.
        /// </summary>
        public async Task OpenAsync(string url, CancellationToken ct)
        {
            _socket?.Dispose();
            _socket = new ClientWebSocket();
            await _socket.ConnectAsync(new Uri(url), ct);
        }

        public async Task SendAsync(string payload, CancellationToken ct)
        {
            ClientWebSocket socket = _socket ?? throw new InvalidOperationException("The socket is not open.");
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            await socket.SendAsync(
                new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, ct);
        }

        public async Task<string> ReceiveAsync(CancellationToken ct)
        {
            ClientWebSocket socket = _socket ?? throw new InvalidOperationException("The socket is not open.");
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(_buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException(
                        $"The socket was closed ({(int)socket.CloseStatus.GetValueOrDefault()}).");

                if (result.Count > 0)
                    stream.Write(_buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        public async Task CloseAsync()
        {
            ClientWebSocket socket = _socket;
            _socket = null;
            if (socket == null)
                return;

            if (socket.State == WebSocketState.Open ||
                socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                }
                catch (Exception)
                {
                    // A socket already torn down by the peer throws here; the dispose below is enough.
                }
            }

            socket.Dispose();
        }
    }
}
