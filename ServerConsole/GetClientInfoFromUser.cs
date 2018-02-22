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

        public async Task<Result<RemoteServer>> RunAsync(AppSettings settings, ConnectionInfo listenServerInfo)
        {
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

            var server = new TplSocketServer(settings);
            server.EventOccurred += HandleServerEvent;

            var randomPort = 0;
            while (randomPort is 0)
            {
                var random = new Random();
                randomPort = random.Next(Program.PortRangeMin, Program.PortRangeMax + 1);

                if (randomPort == listenServerInfo.Port)
                {
                    randomPort = 0;
                }
            }

            var listenTask =
                Task.Run(
                    () => server.HandleIncomingConnectionsAsync(
                        listenServerInfo.GetLocalIpAddress(),
                        randomPort,
                        token),
                    token);

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

            try
            {
                cts.Cancel();
                var serverShutdown = await listenTask.ConfigureAwait(false);
                if (serverShutdown.Failure)
                {
                    Console.WriteLine($"There was an error shutting down the server: {serverShutdown.Error}");
                }
            }
            catch (AggregateException ex)
            {
                Console.WriteLine("\nException messages:");
                foreach (var ie in ex.InnerExceptions)
                {
                    Console.WriteLine($"\t{ie.GetType().Name}: {ie.Message}");
                }
            }
            finally
            {
                server.CloseListenSocket();
            }

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
                case ServerEventType.SendTransferFolderRequestStarted:

                    Console.WriteLine(
                        $"\nSending request for transfer folder path info to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n");
                    
                    break;                    

                case ServerEventType.ReceiveFileListResponseCompleted:

                    Console.WriteLine(
                        $"\nReceived transfer folder path info from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}:\n\tRemote Folder:\t{serverEvent.RemoteFolder}");

                    _clientTransferFolderPath = serverEvent.RemoteFolder;
                    _waitingForTransferFolderResponse = false;

                    break;

                case ServerEventType.SendPublicIpRequestStarted:

                    Console.WriteLine(
                        $"\nSending request for public IP address to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n");

                    break;

                case ServerEventType.ReceivePublicIpResponseCompleted:

                    Console.WriteLine(
                        $"\nReceived public IP address from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}:\n\tRemote Folder:\t{serverEvent.RemoteFolder}");

                    _clientPublicIp = serverEvent.PublicIpAddress;
                    _waitingForPublicIpResponse = false;

                    break;

                case ServerEventType.ErrorOccurred:
                    Console.WriteLine($"Error occurred: {serverEvent.ErrorMessage}");
                    break;
            }
        }
    }
}
