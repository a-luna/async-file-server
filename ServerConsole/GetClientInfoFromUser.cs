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

        public async Task<Result<RemoteServer>> RunAsync(TplSocketServer server, AppSettings settings, ConnectionInfo listenServerInfo)
        {
            server.EventOccurred += HandleServerEvent;

            var cts = new CancellationTokenSource();
            var token = cts.Token;

            var newClient = new RemoteServer();
            var clientInfoIsValid = false;

            while (!clientInfoIsValid)
            {
                var addClientResult = GetNewClientInfoFromUser();
                if (addClientResult.Failure)
                {
                    Console.WriteLine(addClientResult.Error);
                    continue;
                }

                newClient = addClientResult.Value;
                clientInfoIsValid = true;
            }

            var sendFolderRequestResult = 
                await server.RequestTransferFolderPath(
                    newClient.ConnectionInfo.LocalIpAddress, 
                    newClient.ConnectionInfo.Port, 
                    listenServerInfo.LocalIpAddress, 
                    listenServerInfo.Port, 
                    token)
                    .ConfigureAwait(false);

            var sendIpRequestResult = 
                await server.RequestPublicIp(
                    newClient.ConnectionInfo.LocalIpAddress,
                    newClient.ConnectionInfo.Port,
                    listenServerInfo.LocalIpAddress,
                    listenServerInfo.Port,
                    token)
                    .ConfigureAwait(false);

            if (Result.Combine(sendFolderRequestResult, sendIpRequestResult).Failure)
            {
                return Result.Fail<RemoteServer>(
                    $"Error requesting connetion info from new client:\n{sendFolderRequestResult.Error}\n{sendIpRequestResult.Error}");
            }

            while (_waitingForTransferFolderResponse && _waitingForPublicIpResponse) { }

            newClient.TransferFolder = _clientTransferFolderPath;
            newClient.ConnectionInfo.PublicIpAddress = _clientPublicIp;

            Console.WriteLine("Thank you! This server has been successfully configured.");
            return Result.Ok(newClient);
        }

        private Result<RemoteServer> GetNewClientInfoFromUser()
        {
            var clientInfo = new RemoteServer();

            Console.WriteLine("Enter the server's IPv4 address:");
            var input = Console.ReadLine();

            var ipValidationResult = Program.ValidateIpV4Address(input);
            if (ipValidationResult.Failure)
            {
                return Result.Fail<RemoteServer>(ipValidationResult.Error);
            }

            var clientIp = ipValidationResult.Value;
            Console.WriteLine($"Is {clientIp} a local or public IP address?");
            Console.WriteLine("1. Public/External");
            Console.WriteLine("2. Local");
            input = Console.ReadLine();

            var ipTypeValidationResult = Program.ValidateNumberIsWithinRange(input, 1, 2);
            if (ipTypeValidationResult.Failure)
            {
                return Result.Fail<RemoteServer>(ipTypeValidationResult.Error);
            }

            switch (ipTypeValidationResult.Value)
            {
                case Program.PublicIpAddress:
                    clientInfo.ConnectionInfo.PublicIpAddress = clientIp;
                    break;

                case Program.LocalIpAddress:
                    clientInfo.ConnectionInfo.LocalIpAddress = clientIp;
                    break;
            }

            clientInfo.ConnectionInfo.Port =
                Program.GetPortNumberFromUser("Enter the server's port number that handles incoming requests", false);

            return Result.Ok(clientInfo);
        }


        private void HandleServerEvent(ServerEventInfo serverEvent)
        {
            switch (serverEvent.EventType)
            {     
                case ServerEventType.ReceiveFileListResponseCompleted:
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
