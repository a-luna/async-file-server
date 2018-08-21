using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.SocketExtensions;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.Logging;
using AaronLuna.Common.Network;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer
{
    public class ConnectionHandler
    {
        const string NotInitializedMessage =
            "Server is unitialized and cannot handle incoming connections";

        bool _shutdownInitiated;
        ServerSettings _settings;
        readonly Socket _listenSocket;
        readonly Logger _log = new Logger(typeof(ConnectionHandler));

        public ConnectionHandler(ServerSettings settings)
        {
            MyInfo = new ServerInfo
            {
                PortNumber = settings.LocalServerPortNumber,
                TransferFolder = settings.LocalServerFolderPath
            };

            _settings = settings;

            _listenSocket =
                new Socket(
                    AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp);
        }

        public bool IsInitialized { get; private set; }
        public bool IsRunning { get; private set; }
        public ServerInfo MyInfo { get; }

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<string> ErrorOccurred;
        public event EventHandler<Socket> AcceptedSocketConnection;

        public void RegisterEventHandlers(object registrant)
        {
            if (registrant is AsyncServer server)
            {
                server.ShutdownInitiated += HandleShutdownRequested;
            }
        }

        private void HandleShutdownRequested(object sender, EventArgs e)
        {
            _shutdownInitiated = true;
        }

        public void UpdateSettings(ServerSettings settings)
        {
            _settings = settings;

            MyInfo.TransferFolder = _settings.LocalServerFolderPath;
        }

        public async Task InitializeAsync(string name = "AsyncFileServer")
        {
            if (IsInitialized) return;

            MyInfo.Name = name;
            MyInfo.Platform = Environment.OSVersion.Platform.ToServerPlatform();
            MyInfo.TransferFolder = _settings.LocalServerFolderPath;
            MyInfo.PortNumber = _settings.LocalServerPortNumber;

            var getLocalIp =
                NetworkUtilities.GetLocalIPv4Address(_settings.LocalNetworkCidrIp);

            MyInfo.LocalIpAddress = getLocalIp.Success
                ? getLocalIp.Value
                : IPAddress.Loopback;

            var getPublicIp =
                await NetworkUtilities.GetPublicIPv4AddressAsync().ConfigureAwait(false);

            MyInfo.PublicIpAddress = getPublicIp.Success
                ? getPublicIp.Value
                : IPAddress.None;

            if (getLocalIp.Success)
            {
                MyInfo.SessionIpAddress = MyInfo.LocalIpAddress;
            }
            else if (getPublicIp.Success)
            {
                MyInfo.SessionIpAddress = MyInfo.PublicIpAddress;
            }

            IsInitialized = true;
        }

        public async Task<Result> RunAsync(CancellationToken token)
        {
            if (!IsInitialized)
            {
                return Result.Fail(NotInitializedMessage);
            }

            var listenOnLocalPort = Listen();
            if (listenOnLocalPort.Failure)
            {
                ReportError(listenOnLocalPort.Error);
                return listenOnLocalPort;
            }

            return await HandleIncomingRequestsAsync(token).ConfigureAwait(false);
        }

        Result Listen()
        {
            var ipEndPoint = new IPEndPoint(IPAddress.Any, MyInfo.PortNumber);
            try
            {
                _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listenSocket.Bind(ipEndPoint);
                _listenSocket.Listen(_settings.SocketSettings.ListenBacklogSize);
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method Listen", ex);
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method AsyncFileServer.Listen)");
            }

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.ServerStartedListening,
                LocalPortNumber = MyInfo.PortNumber
            });

            return Result.Ok();
        }

        async Task<Result> HandleIncomingRequestsAsync(CancellationToken token)
        {
            // Main loop. Server handles incoming connections until shutdown command is received
            // or cancellation is requested

            IsRunning = true;
            while (RunServer(token))
            {
                var acceptConnection = await AcceptConnectionFromRemoteServerAsync(token).ConfigureAwait(false);
                if (acceptConnection.Failure)
                {
                    ReportError(acceptConnection.Error);
                    continue;
                }

                if (!RunServer(token)) break;

                var newSocket = acceptConnection.Value;
                AcceptedSocketConnection?.Invoke(this, newSocket);
            }

            ShutdownListenSocket();
            IsRunning = false;

            return Result.Ok();
        }

        bool RunServer(CancellationToken token)
        {
            return !token.IsCancellationRequested && !_shutdownInitiated;
        }

        void ReportError(string error)
        {
            ErrorOccurred?.Invoke(this, error);
        }

        async Task<Result<Socket>> AcceptConnectionFromRemoteServerAsync(CancellationToken token)
        {
            Result<Socket> acceptConnection;
            try
            {
                acceptConnection = await _listenSocket.AcceptTaskAsync(token).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex)
            {
                ReportError(ex.GetReport());
                return Result.Fail<Socket>(ex.GetReport());
            }
            catch (SocketException ex)
            {
                ReportError(ex.GetReport());
                return Result.Fail<Socket>(ex.GetReport());
            }

            if (acceptConnection.Failure)
            {
                ReportError(acceptConnection.Error);
                return Result.Fail<Socket>(acceptConnection.Error);
            }

            var socket = acceptConnection.Value;
            var remoteServerIpString = socket.RemoteEndPoint.ToString().Split(':')[0];
            var remoteServerIpAddress = NetworkUtilities.ParseSingleIPv4Address(remoteServerIpString).Value;

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.ConnectionAccepted,
                RemoteServerIpAddress = remoteServerIpAddress
            });

            return Result.Ok(socket);
        }

        void ShutdownListenSocket()
        {
            EventOccurred?.Invoke(this,
                new ServerEvent { EventType = EventType.ShutdownListenSocketStarted });

            try
            {
                _listenSocket.Shutdown(SocketShutdown.Both);
                _listenSocket.Close();
            }
            catch (SocketException ex)
            {
                EventOccurred?.Invoke(this,
                    new ServerEvent { EventType = EventType.ShutdownListenSocketCompletedWithError });

                EventOccurred?.Invoke(this,
                    new ServerEvent { EventType = EventType.ServerStoppedListening });

                ReportError(ex.GetReport());
                return;
            }

            EventOccurred?.Invoke(this,
                new ServerEvent { EventType = EventType.ShutdownListenSocketCompletedWithoutError });

            EventOccurred?.Invoke(this,
                new ServerEvent { EventType = EventType.ServerStoppedListening });
        }
    }
}
