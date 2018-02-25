namespace ServerConsole
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using AaronLuna.Common.Result;
    using TplSocketServer;

    public class GetClientInfoFromUser
    {
        bool _waitingForTransferFolderResponse = true;
        bool _waitingForPublicIpResponse = true;

        string _clientTransferFolderPath = string.Empty;
        string _clientPublicIp = string.Empty;

        public event ServerEventDelegate EventOccurred;

        public async Task<Result<RemoteServer>> RunAsync(TplSocketServer server, ConnectionInfo listenServerInfo)
        {
            var cts = new CancellationTokenSource();
            var token = cts.Token;

            server.EventOccurred += HandleServerEvent;

            var newClient = new RemoteServer();
            var clientInfoIsValid = false;

            while (!clientInfoIsValid)
            {
                var addClientResult = Program.GetRemoteServerConnectionInfoFromUser();
                if (addClientResult.Failure)
                {
                    return addClientResult;
                }

                newClient = addClientResult.Value;
                clientInfoIsValid = true;
            }

            var clientIp = string.Empty;
            if (!string.IsNullOrEmpty(newClient.ConnectionInfo.LocalIpAddress))
            {
                clientIp = newClient.ConnectionInfo.LocalIpAddress;
            }

            if (string.IsNullOrEmpty(clientIp) && !string.IsNullOrEmpty(newClient.ConnectionInfo.PublicIpAddress))
            {
                clientIp = newClient.ConnectionInfo.PublicIpAddress;
            }

            if (string.IsNullOrEmpty(clientIp))
            {
                return Result.Fail<RemoteServer>("There was an error getting the client's IP address from user input.");
            }

            var sendFolderRequestResult = 
                await server.RequestTransferFolderPath(
                    clientIp, 
                    newClient.ConnectionInfo.Port, 
                    listenServerInfo.LocalIpAddress, 
                    listenServerInfo.Port,
                    token)
                    .ConfigureAwait(false);

            if (sendFolderRequestResult.Failure)
            {
                return Result.Fail<RemoteServer>(
                    $"Error requesting transfer folder path from new client:\n{sendFolderRequestResult.Error}");
            }

            while (_waitingForTransferFolderResponse) { }
            newClient.TransferFolder = _clientTransferFolderPath;

            if (string.IsNullOrEmpty(newClient.ConnectionInfo.PublicIpAddress))
            {
                var sendIpRequestResult =
                    await server.RequestPublicIp(
                            clientIp,
                            newClient.ConnectionInfo.Port,
                            listenServerInfo.LocalIpAddress,
                            listenServerInfo.Port,
                            token)
                        .ConfigureAwait(false);

                if (sendIpRequestResult.Failure)
                {
                    return Result.Fail<RemoteServer>(
                        $"Error requesting transfer folder path from new client:\n{sendIpRequestResult.Error}");
                }

                while (_waitingForPublicIpResponse) { }
                newClient.ConnectionInfo.PublicIpAddress = _clientPublicIp;
            }

            return Result.Ok(newClient);
        }

        private void HandleServerEvent(ServerEventInfo serverEvent)
        {
            EventOccurred?.Invoke(serverEvent);

            switch (serverEvent.EventType)
            {     
                case ServerEventType.ReceiveTransferFolderResponseCompleted:
                    _clientTransferFolderPath = serverEvent.RemoteFolder;
                    _waitingForTransferFolderResponse = false;
                    break;

                case ServerEventType.ReceivePublicIpResponseCompleted:
                    _clientPublicIp = serverEvent.PublicIpAddress;
                    _waitingForPublicIpResponse = false;
                    break;
            }
        }
    }
}
