using System;
using System.Collections.Generic;
using FishNet.Transporting;

namespace RadiantPool.Game
{
    /// <summary>In-process FishNet transport for platforms without sockets (WebGL).
    /// The listen-server and its local client exchange packets through plain queues —
    /// no UDP, no TCP, no threads — so a solo "host a campaign" works in a browser.
    /// FishNet's own offline transport (Yak) ships as a non-functional stub in the free
    /// tier (empty Initialize, sockets never created), hence this replacement. The
    /// WebGL build swaps Tugboat for this component at build time (WebGLBuildSupport);
    /// desktop builds never contain it. Remote joins are impossible by design: a browser
    /// cannot listen for connections, so the title screen hides Join on this transport.</summary>
    public class LoopbackTransport : Transport
    {
        // Matches Yak's intended MTU; anything larger is fragmented by FishNet.
        private const int Mtu = 5000;
        private const int LocalClientId = 0;

        private LocalConnectionState _serverState = LocalConnectionState.Stopped;
        private LocalConnectionState _clientState = LocalConnectionState.Stopped;

        private readonly Queue<(byte channel, byte[] data)> _toServer = new Queue<(byte, byte[])>();
        private readonly Queue<(byte channel, byte[] data)> _toClient = new Queue<(byte, byte[])>();

        public override event Action<ClientConnectionStateArgs> OnClientConnectionState;
        public override event Action<ServerConnectionStateArgs> OnServerConnectionState;
        public override event Action<RemoteConnectionStateArgs> OnRemoteConnectionState;
        public override event Action<ClientReceivedDataArgs> OnClientReceivedData;
        public override event Action<ServerReceivedDataArgs> OnServerReceivedData;

        public override string GetConnectionAddress(int connectionId) => "loopback";

        public override void HandleClientConnectionState(ClientConnectionStateArgs connectionStateArgs)
            => OnClientConnectionState?.Invoke(connectionStateArgs);

        public override void HandleServerConnectionState(ServerConnectionStateArgs connectionStateArgs)
            => OnServerConnectionState?.Invoke(connectionStateArgs);

        public override void HandleRemoteConnectionState(RemoteConnectionStateArgs connectionStateArgs)
            => OnRemoteConnectionState?.Invoke(connectionStateArgs);

        public override void HandleClientReceivedDataArgs(ClientReceivedDataArgs receivedDataArgs)
            => OnClientReceivedData?.Invoke(receivedDataArgs);

        public override void HandleServerReceivedDataArgs(ServerReceivedDataArgs receivedDataArgs)
            => OnServerReceivedData?.Invoke(receivedDataArgs);

        public override LocalConnectionState GetConnectionState(bool server)
            => server ? _serverState : _clientState;

        public override RemoteConnectionState GetConnectionState(int connectionId)
            => connectionId == LocalClientId && _clientState == LocalConnectionState.Started
                ? RemoteConnectionState.Started
                : RemoteConnectionState.Stopped;

        public override bool IsLocalTransport(int connectionid) => true;

        public override int GetMTU(byte channel) => Mtu;

        public override bool StartConnection(bool server) => server ? StartServer() : StartClient();

        private bool StartServer()
        {
            if (_serverState != LocalConnectionState.Stopped) return false;
            SetServerState(LocalConnectionState.Starting);
            SetServerState(LocalConnectionState.Started);
            return true;
        }

        private bool StartClient()
        {
            // Loopback has nothing to dial: the server must live in this same process.
            if (_serverState != LocalConnectionState.Started) return false;
            if (_clientState != LocalConnectionState.Stopped) return false;
            SetClientState(LocalConnectionState.Starting);
            SetClientState(LocalConnectionState.Started);
            HandleRemoteConnectionState(
                new RemoteConnectionStateArgs(RemoteConnectionState.Started, LocalClientId, Index));
            return true;
        }

        public override bool StopConnection(bool server) => server ? StopServer() : StopClient();

        public override bool StopConnection(int connectionId, bool immediately)
            => connectionId == LocalClientId && StopClient();

        public override void Shutdown()
        {
            StopClient();
            StopServer();
        }

        private bool StopClient()
        {
            if (_clientState == LocalConnectionState.Stopped) return false;
            SetClientState(LocalConnectionState.Stopping);
            SetClientState(LocalConnectionState.Stopped);
            _toServer.Clear();
            _toClient.Clear();
            if (_serverState == LocalConnectionState.Started)
                HandleRemoteConnectionState(
                    new RemoteConnectionStateArgs(RemoteConnectionState.Stopped, LocalClientId, Index));
            return true;
        }

        private bool StopServer()
        {
            if (_serverState == LocalConnectionState.Stopped) return false;
            StopClient();
            SetServerState(LocalConnectionState.Stopping);
            SetServerState(LocalConnectionState.Stopped);
            return true;
        }

        private void SetServerState(LocalConnectionState state)
        {
            if (_serverState == state) return;
            _serverState = state;
            HandleServerConnectionState(new ServerConnectionStateArgs(state, Index));
        }

        private void SetClientState(LocalConnectionState state)
        {
            if (_clientState == state) return;
            _clientState = state;
            HandleClientConnectionState(new ClientConnectionStateArgs(state, Index));
        }

        public override void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            if (_clientState != LocalConnectionState.Started) return;
            _toServer.Enqueue((channelId, Copy(segment)));
        }

        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            if (connectionId != LocalClientId || _serverState != LocalConnectionState.Started) return;
            _toClient.Enqueue((channelId, Copy(segment)));
        }

        // FishNet reuses its send buffer the moment this call returns, so every packet is
        // copied; delivery happens on the next IterateIncoming, like a socket poll would.
        private static byte[] Copy(ArraySegment<byte> segment)
        {
            var data = new byte[segment.Count];
            Buffer.BlockCopy(segment.Array, segment.Offset, data, 0, segment.Count);
            return data;
        }

        public override void IterateIncoming(bool asServer)
        {
            if (asServer)
            {
                while (_serverState == LocalConnectionState.Started && _toServer.Count > 0)
                {
                    var (channel, data) = _toServer.Dequeue();
                    HandleServerReceivedDataArgs(new ServerReceivedDataArgs(
                        new ArraySegment<byte>(data), (Channel)channel, LocalClientId, Index));
                }
            }
            else
            {
                while (_clientState == LocalConnectionState.Started && _toClient.Count > 0)
                {
                    var (channel, data) = _toClient.Dequeue();
                    HandleClientReceivedDataArgs(new ClientReceivedDataArgs(
                        new ArraySegment<byte>(data), (Channel)channel, Index));
                }
            }
        }

        public override void IterateOutgoing(bool asServer)
        {
            // Sends are enqueued directly; nothing to flush.
        }
    }
}
