namespace ServerConsole
{
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Numeric;
    using AaronLuna.Common.Result;

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using TplSocketServer;

    class Program
    {
        const string SavedClientsFileName = "settings.xml";

        const int SendTextMessage = 1;
        const int SendFile = 2;
        const int GetFile = 3;
        const int ShutDown = 4;

        const int PublicIpAddress = 1;
        const int LocalIpAddress = 2;

        const int PortRangeMin = 49152;
        const int PortRangeMax = 65535;

        static async Task Main()
        {
            var settingsFilePath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SavedClientsFileName}";
            var settings = ServerSettings.Deserialize(settingsFilePath);
            
            var cts = new CancellationTokenSource();
            var token = cts.Token;

            var server = 
                new TplSocketServer(
                    settings.SocketSettings.MaxNumberOfConections,
                    settings.SocketSettings.BufferSize,
                    settings.SocketSettings.ConnectTimeoutMs,
                    settings.SocketSettings.ReceiveTimeoutMs,
                    settings.SocketSettings.SendTimeoutMs);

            server.EventOccurred += HandleServerEvent;

            try
            {
                var myInfo = await GetThisServerInfo(settings);
                var listenTask = Task.Run(() => server.HandleIncomingConnectionsAsync(myInfo.Port, token), token);

                var menuTask = await ServerMenu(server, myInfo, settings, token);
                if (menuTask.Failure)
                {
                    Console.WriteLine(menuTask.Error);
                }

                ServerSettings.Serialize(settings, settingsFilePath);

                cts.Cancel();
                var serverShutdown = await listenTask;
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

            Console.WriteLine("Press enter to exit.");
            Console.ReadLine();
        }

        private static async Task<ServerInfo> GetThisServerInfo(ServerSettings settings)
        {
            var myInfo = new ServerInfo
            {
                LocalIpAddress = GetLocalIpToBindTo(),
                Port = settings.PortNumber,
                TransferFolder = settings.TransferFolderPath
            };

            var getPublicIpResult = await ServerInfo.GetPublicIpAddressAsync();
            if (getPublicIpResult.Failure)
            {
                Console.WriteLine("Unable to determine public IP address, this server will only be able to communicate with machines in the same local network.");
            }
            else
            {
                myInfo.PublicIpAddress = getPublicIpResult.Value;
            }

            return myInfo;
        }

        private static string GetLocalIpToBindTo()
        {
            var localIps = IpAddressHelper.GetAllLocalIpv4Addresses();
            if (localIps.Count == 1)
            {
                return localIps[0].ToString();
            }

            var ipChoice = 0;
            int totalMenuChoices = localIps.Count;
            while (ipChoice == 0)
            {
                Console.WriteLine("There are multiple IPv4 addresses available on this machine, choose the most appropriate local address:");

                foreach (var i in Enumerable.Range(0, localIps.Count))
                {
                    Console.WriteLine($"{i + 1}. {localIps[i]}");
                }

                var input = Console.ReadLine();

                var validationResult = ValidateNumberIsWithinRange(input, 1, totalMenuChoices);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                ipChoice = validationResult.Value;
            }

            return localIps[ipChoice - 1].ToString();
        }

        private static async Task<Result> ServerMenu(TplSocketServer server, ServerInfo myInfo, ServerSettings settings, CancellationToken token)
        {
            Console.WriteLine(string.Empty);

            while (true)
            {
                Console.WriteLine($"Server is ready to handle incoming requests. My endpoint is {myInfo.GetLocalEndPoint()}\n");

                var menuResult = GetMenuChoice();
                if (menuResult.Failure)
                {
                    Console.WriteLine(menuResult.Error);
                    continue;
                }

                var menuChoice = menuResult.Value;
                if (menuChoice == ShutDown)
                {
                    Console.WriteLine("Server is shutting down");
                    break;
                }

                var chooseClientResult = ChooseClient(settings);
                if (chooseClientResult.Failure)
                {
                    continue;
                }

                var clientInfo = chooseClientResult.Value;
                var ipChoice = ChoosePublicOrLocalIpAddress(clientInfo);

                var result = Result.Ok();
                switch (menuChoice)
                {
                    case SendTextMessage:
                        result = await SendTextMessageToClientAsync(server, myInfo, clientInfo, ipChoice, token);
                        break;

                    case SendFile:
                        result = await SendFileToClientAsync(server, myInfo, clientInfo, ipChoice, token);
                        break;

                    case GetFile:
                        break;
                }

                if (result.Failure)
                {
                    return result;
                }
            }

            return Result.Ok();
        }

        private static Result<int> GetMenuChoice()
        {
            WriteMenuToScreen();
            var input = Console.ReadLine();
            Console.WriteLine(string.Empty);

            var validationResult = ValidateNumberIsWithinRange(input, 1, 4);
            if (validationResult.Failure)
            {
                return validationResult;
            }
            
            return Result.Ok(validationResult.Value);
        }

        private static void WriteMenuToScreen()
        {   
            Console.WriteLine("Please make a choice from the menu below:");
            Console.WriteLine("1. Send Text Message");
            Console.WriteLine("2. Send File");
            Console.WriteLine("3. Get File");
            Console.WriteLine("4. Shutdown");
        }
        
        private static Result<ServerInfo> ChooseClient(ServerSettings settings)
        {
            var clientMenuChoice = 0;
            var totalMenuChoices = settings.RemoteServers.Count + 2;
            var addNewClient = settings.RemoteServers.Count + 1;
            var returnToMainMenu = totalMenuChoices;

            while (clientMenuChoice == 0)
            {
                Console.WriteLine("Choose a remote server for this request:");

                foreach (var i in Enumerable.Range(0, settings.RemoteServers.Count))
                {
                    var thisClient = settings.RemoteServers[i];
                    Console.WriteLine($"{i + 1}. Local IP: [{thisClient.GetPublicEndPoint()}]\tPublic IP: [{thisClient.GetLocalEndPoint()}]");
                }                

                Console.WriteLine($"{addNewClient}. Add New Client");
                Console.WriteLine($"{returnToMainMenu}. Return to Main Menu");

                var input = Console.ReadLine();
                Console.WriteLine(string.Empty);

                var validationResult = ValidateNumberIsWithinRange(input, 1, totalMenuChoices);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                clientMenuChoice = validationResult.Value;
            }

            if (clientMenuChoice == returnToMainMenu)
            {
                // User chooses to return to main menu, we will not display an
                // error on the console in this case
                return Result.Fail<ServerInfo>(string.Empty);
            }

            if (clientMenuChoice == addNewClient)
            {
                return AddNewClient(settings);
            }
            
            return Result.Ok(settings.RemoteServers[clientMenuChoice - 1]);
        }

        private static Result<ServerInfo> AddNewClient(ServerSettings settings)
        {
            ServerInfo newClientInfo = new ServerInfo();
            var clientInfoIsValid = false;

            while (!clientInfoIsValid)
            {
                var addClientResult = GetNewClientInfoFromUser();
                if (addClientResult.Failure)
                {
                    Console.WriteLine(addClientResult.Error);
                    continue;
                }

                newClientInfo = addClientResult.Value;
                settings.RemoteServers.Add(newClientInfo);

                clientInfoIsValid = true;
            }

            return Result.Ok(newClientInfo);
        }

        private static Result<ServerInfo> GetNewClientInfoFromUser()
        {
            Console.WriteLine("Enter the server's public IPv4 address:");
            var input = Console.ReadLine();

            var ipValidationResult = ValidateIpV4Address(input);
            if (ipValidationResult.Failure)
            {
                return Result.Fail<ServerInfo>(ipValidationResult.Error);
            }

            var clientIp = ipValidationResult.Value;

            Console.WriteLine($"Enter the server's port number that handles incoming requests (range {PortRangeMin}-{PortRangeMax}):");
            input = Console.ReadLine();

            var portValidationResult = ValidateNumberIsWithinRange(input, PortRangeMin, PortRangeMax);
            if (ipValidationResult.Failure)
            {
                return Result.Fail<ServerInfo>(ipValidationResult.Error);
            }

            var clientPort = portValidationResult.Value;

            Console.WriteLine("Enter the path of the transfer folder on the server:");
            input = Console.ReadLine();

            var clientInfo = new ServerInfo
            {
                PublicIpAddress = clientIp,
                Port = clientPort,
                TransferFolder = input
            };

            return Result.Ok(clientInfo);
        }

        private static int ChoosePublicOrLocalIpAddress(ServerInfo serverInfo)
        {
            var ipChoice = 0;
            while (ipChoice == 0)
            {
                Console.WriteLine("Which IP address would you like to use for this request?");
                Console.WriteLine($"1. Public IP ({serverInfo.PublicIpAddress})");
                Console.WriteLine($"2. Local IP ({serverInfo.LocalIpAddress})");

                var input = Console.ReadLine();
                Console.WriteLine(string.Empty);

                var validationResult = ValidateNumberIsWithinRange(input, 1, 2);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                ipChoice = validationResult.Value;
            }

            return ipChoice;
        }

        private static async Task<Result> SendTextMessageToClientAsync(TplSocketServer server, ServerInfo myInfo, ServerInfo clientInfo, int ipChoice, CancellationToken token)
        {
            Console.WriteLine($"Please enter a text message to send to {clientInfo.GetLocalEndPoint()}");
            var message = Console.ReadLine();
            Console.WriteLine(string.Empty);

            var clientIp = GetChosenIpAddress(clientInfo, ipChoice);

            var sendMessageResult =
                await server.SendTextMessageAsync(
                    message,
                    clientIp,
                    clientInfo.Port,
                    myInfo.LocalIpAddress,
                    myInfo.Port,
                    token);

            return sendMessageResult.Failure ? sendMessageResult : Result.Ok();
        }

        private static async Task<Result> SendFileToClientAsync(
            TplSocketServer server, 
            ServerInfo myInfo,
            ServerInfo clientInfo,
            int ipChoice,
            CancellationToken token)
        {
            var selectFileResult = SelectFileFromTransferFolder(myInfo.TransferFolder);
            if (selectFileResult.Failure)
            {
                Console.WriteLine(selectFileResult.Error);
                return Result.Ok();
            }

            var fileToSend = selectFileResult.Value;
            var clientIp = GetChosenIpAddress(clientInfo, ipChoice);

            var sendFileResult = 
                await server.SendFileAsync(
                    clientIp, 
                    clientInfo.Port, 
                    fileToSend,
                    clientInfo.TransferFolder, 
                    token);

            return sendFileResult.Failure ? sendFileResult : Result.Ok();
        }

        private static Result<string> SelectFileFromTransferFolder(string transferFolderPath)
        {
            List<string> listOfFiles;
            try
            {
                listOfFiles = Directory.GetFiles(transferFolderPath).ToList();
            }
            catch (IOException ex)
            {
                return Result.Fail<string>($"{ex.Message} ({ex.GetType()})");
            }

            if (!listOfFiles.Any())
            {
                return Result.Fail<string>(
                    $"Transfer folder is empty, please place files in the path below:\n{transferFolderPath}");
            }

            int fileMenuChoice = 0;
            int totalMenuChoices = listOfFiles.Count + 1;
            int returnToPreviousMenu = totalMenuChoices;

            while (fileMenuChoice == 0)
            {
                Console.WriteLine("Choose a file to send:");

                foreach (var i in Enumerable.Range(0, listOfFiles.Count))
                {
                    var fileName = Path.GetFileName(listOfFiles[i]);
                    var fileSize = new FileInfo(listOfFiles[i]).Length;
                    Console.WriteLine($"{i + 1}. {fileName} ({fileSize.ConvertBytesForDisplay()})");
                }
                
                Console.WriteLine($"{returnToPreviousMenu}. Return to Previous Menu");

                var input = Console.ReadLine();
                Console.WriteLine(string.Empty);

                var validationResult = ValidateNumberIsWithinRange(input, 1, totalMenuChoices);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                fileMenuChoice = validationResult.Value;
            }

            if (fileMenuChoice == returnToPreviousMenu)
            {
                return Result.Fail<string>("Returning to main menu");
            }

            return Result.Ok(listOfFiles[fileMenuChoice - 1]);
        }

        private static string GetChosenIpAddress(ServerInfo clientInfo, int ipChoice)
        {
            var clientIp = string.Empty;
            if (ipChoice == PublicIpAddress)
            {
                clientIp = clientInfo.PublicIpAddress;
            }

            if (ipChoice == LocalIpAddress)
            {
                clientIp = clientInfo.LocalIpAddress;
            }

            return clientIp;
        }

        private static Result<string> ValidateIpV4Address(string input)
        {
            var parseIpResult = IpAddressHelper.ParseSingleIPv4Address(input);
            if (parseIpResult.Failure)
            {
                return Result.Fail<string>($"Unable tp parse IPv4 address from input string: {parseIpResult.Error}");
            }

            return Result.Ok(parseIpResult.Value);
        }

        private static Result<int> ValidateNumberIsWithinRange(string input, int rangeMin, int rangeMax)
        {
            if (string.IsNullOrEmpty(input))
            {
                return Result.Fail<int>("Error! Input was null or empty string.");
            }

            if (!int.TryParse(input, out int parsedNum))
            {
                return Result.Fail<int>($"Unable to parse int value from input string: {input}");
            }

            if (parsedNum < rangeMin || parsedNum > rangeMax)
            {
                return Result.Fail<int>($"{parsedNum} is not within allowed range {rangeMin}-{rangeMax}");
            }

            return Result.Ok(parsedNum);
        }

        private static void HandleServerEvent(ServerEventInfo serverEvent)
        {
            switch (serverEvent.EventType)
            {
                case ServerEventType.ReceiveTextMessageCompleted:
                    Console.WriteLine($"\nMessage received from client {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}:");
                    Console.WriteLine($"{serverEvent.TextMessage}\n");
                    WriteMenuToScreen();
                    break;

                case ServerEventType.ReceiveOutboundFileTransferInfoCompleted:
                    Console.WriteLine("\nReceived Outbound File Transfer Request");
                    Console.WriteLine($"\tFile Requested:\t\t{serverEvent.FileName}\n\tFile Size:\t\t\t{serverEvent.FileSizeString}\n\tRemote Endpoint:\t{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n\tTarget Directory:\t{serverEvent.RemoteFolder}\n");
                    WriteMenuToScreen();
                    break;

                case ServerEventType.ReceiveInboundFileTransferInfoCompleted:
                    Console.WriteLine("\nReceived Inbound File Transfer Request");
                    Console.WriteLine($"\tFile Name:\t\t\t{serverEvent.FileName}\n\tFile Size:\t\t\t{serverEvent.FileSizeString}\n");
                    WriteMenuToScreen();
                    break;

                case ServerEventType.SendFileBytesStarted:
                    Console.WriteLine("Sending file to client...");
                    break;

                case ServerEventType.ReceiveFileBytesStarted:
                    Console.WriteLine("\nReceving file from client...");
                    break;

                case ServerEventType.FileTransferProgress:
                    Console.WriteLine($"File Transfer {serverEvent.PercentComplete:P0} Complete");
                    break;

                case ServerEventType.ReceiveConfirmationMessageCompleted:
                    Console.WriteLine("Client confirmed file transfer successfully completed\n");
                    WriteMenuToScreen();
                    break;

                case ServerEventType.ReceiveFileBytesCompleted:
                    Console.WriteLine("Successfully received file from client");
                    Console.WriteLine($"\tTransfer Start Time:\t{serverEvent.FileTransferStartTime.ToLongTimeString()}\n\tTransfer Complete Time:\t{serverEvent.FileTransferCompleteTime.ToLongTimeString()}\n\tElapsed Time:\t\t\t{serverEvent.FileTransferElapsedTimeString}\n\tTransfer Rate:\t\t\t{serverEvent.FileTransferRate}\n");
                    WriteMenuToScreen();
                    break;

                case ServerEventType.ShutdownListenSocketCompleted:
                    Console.WriteLine("Server has been successfully shutdown");
                    break;

                case ServerEventType.ErrorOccurred:
                    Console.WriteLine($"Error occurred: {serverEvent.ErrorMessage}");
                    break;
            }
        }
    }
}
