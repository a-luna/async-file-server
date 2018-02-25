namespace ServerConsole
{
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Numeric;
    using AaronLuna.Common.Result;

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;

    using TplSocketServer;

    class Program
    {
        const string SettingsFileName = "settings.xml";
        const string DefaultTransferFolderName = "transfer";
        const string IpChoiceClient = "Which IP address would you like to use for this request?";
        const string NotifyLanTrafficOnly = "Unable to determine public IP address, this server will only be able to communicate with machines in the same local network.";
        const string PropmptMultipleLocalIPv4Addresses = "There are multiple IPv4 addresses available on this machine, choose the most appropriate local address:";

        const int SendMessage = 1;
        const int SendFile = 2;
        const int GetFile = 3;
        const int ShutDown = 4;

        public const int LocalIpAddress = 1;
        public const int PublicIpAddress = 2;        

        public const int PortRangeMin = 49152;
        public const int PortRangeMax = 65535;

        static async Task Main()
        {
            TplSocketServer listenServer = new TplSocketServer();
            
            try
            {
                var settings = InitializeAppSettings();
                var myInfo = await ConfigureListenServerAsync();
                listenServer = new TplSocketServer(settings);
                listenServer.EventOccurred += HandleServerEvent;

                var cts = new CancellationTokenSource();
                var token = cts.Token;

                var listenTask =
                    Task.Run(
                        () => listenServer.HandleIncomingConnectionsAsync(
                            myInfo.GetLocalIpAddress(), 
                            myInfo.Port, 
                            token),
                        token);

                await ServerMenuAsync(listenServer, myInfo, settings, token);

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
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message} ({ex.GetType()}) raised in method Program.Main (Top-level try/catch block)");
            }
            finally
            {
                listenServer.CloseListenSocket();
            }

            Console.WriteLine("Press enter to exit.");
            Console.ReadLine();
        }

        public static AppSettings InitializeAppSettings()
        {
            var settings = new AppSettings
            {
                TransferFolderPath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{DefaultTransferFolderName}"
            };

            var settingsFilePath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SettingsFileName}";
            if (File.Exists(settingsFilePath))
            {
                settings = AppSettings.Deserialize(settingsFilePath);
            }

            return settings;
        }

        private static async Task<ConnectionInfo> ConfigureListenServerAsync()
        {
            var portChoice =
                GetPortNumberFromUser("Enter a port number where this server will listen for incoming connections", true);

            var localIp = GetLocalIpToBindTo();
            var publicIp = IPAddress.None;

            var retrievePublicIp = await IpAddressHelper.GetPublicIPv4AddressAsync();
            if (retrievePublicIp.Failure)
            {
                Console.WriteLine(NotifyLanTrafficOnly);
            }
            else
            {
                publicIp = retrievePublicIp.Value;
            }
            
            return new ConnectionInfo
            {
                LocalIpAddress = localIp,
                PublicIpAddress = publicIp.ToString(),
                Port = portChoice
            };
        }

        private static string GetLocalIpToBindTo()
        {
            var localIps = IpAddressHelper.GetLocalIPv4AddressList();
            if (localIps.Count == 1)
            {
                return localIps[0].ToString();
            }

            var ipChoice = 0;
            var totalMenuChoices = localIps.Count;
            while (ipChoice == 0)
            {
                Console.WriteLine(PropmptMultipleLocalIPv4Addresses);

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

        private static async Task<Result> ServerMenuAsync(TplSocketServer server, ConnectionInfo myInfo, AppSettings settings, CancellationToken token)
        {
            Console.WriteLine(string.Empty);

            // TODO: Change logic so that main menu does not always reprint when operation (GetFIle) is in progress
            while (true)
            {
                Console.WriteLine($"Server is ready to handle incoming requests\n\n\tLocal Endpoint:\t\t{myInfo.GetLocalEndPoint()}\n\tPublic Endpoint:\t{myInfo.GetPublicEndPoint()}");

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

                var chooseClientResult = await ChooseClientAsync(server, settings, myInfo);
                if (chooseClientResult.Failure)
                {
                    Console.WriteLine(chooseClientResult.Error);
                    continue;
                }

                var clientInfo = chooseClientResult.Value;
                var ipChoice = ChoosePublicOrLocalIpAddress(clientInfo, IpChoiceClient);
                var clientIpAddress = GetChosenIpAddress(clientInfo.ConnectionInfo, ipChoice);

                var result = Result.Ok();
                switch (menuChoice)
                {   
                    case SendMessage:
                        result = await SendTextMessageToClientAsync(
                                        server, 
                                        myInfo, 
                                        clientInfo.ConnectionInfo, 
                                        ipChoice, 
                                        token);
                        break;

                    case SendFile:
                        result = await SendFileToClientAsync(
                                        server, 
                                        myInfo, 
                                        clientInfo.ConnectionInfo, 
                                        settings.TransferFolderPath, 
                                        ipChoice, 
                                        token);
                        break;

                    case GetFile:
                        result = await RequestFileListFromClientAsync(
                                        settings, 
                                        myInfo,
                                        clientIpAddress,
                                        clientInfo.ConnectionInfo.Port);
                        break;
                }

                if (result.Success) continue;

                Console.WriteLine(result.Error);
                if (UserWantsToShutdownServer())
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
        
        private static async Task<Result<RemoteServer>> ChooseClientAsync(TplSocketServer server, AppSettings settings, ConnectionInfo myInfo)
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
                    Console.WriteLine($"{i + 1}. Local IP: [{thisClient.ConnectionInfo.GetLocalEndPoint()}]\tPublic IP: [{thisClient.ConnectionInfo.GetPublicEndPoint()}]");
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
                return Result.Fail<RemoteServer>(string.Empty);
            }

            if (clientMenuChoice == addNewClient)
            {
                return await AddNewClientAsync(server, settings, myInfo);
            }
            
            return Result.Ok(settings.RemoteServers[clientMenuChoice - 1]);
        }

        private static async Task<Result<RemoteServer>> AddNewClientAsync(TplSocketServer server, AppSettings settings, ConnectionInfo myInfo)
        {
            var getNewClientInfo = new GetClientInfoFromUser();
            getNewClientInfo.EventOccurred += HandleServerEvent;

            server.RemoveAllSubscribers();
            var getNewClientInfoResult = await getNewClientInfo.RunAsync(server, myInfo);

            server.RemoveAllSubscribers();
            server.EventOccurred += HandleServerEvent;

            if (getNewClientInfoResult.Failure)
            {
                return Result.Fail<RemoteServer>(getNewClientInfoResult.Error);
            }

            Console.WriteLine("Thank you! This server has been successfully configured.");
            var newClientInfo = getNewClientInfoResult.Value;

            settings.RemoteServers.Add(newClientInfo);
            SaveSettingsToFile(settings);
            return Result.Ok(newClientInfo);
        }

        public static Result<RemoteServer> GetRemoteServerConnectionInfoFromUser()
        {
            var remoteServerInfo = new RemoteServer();

            Console.WriteLine("Enter the server's IPv4 address:");
            var input = Console.ReadLine();

            var ipValidationResult = ValidateIpV4Address(input);
            if (ipValidationResult.Failure)
            {
                return Result.Fail<RemoteServer>(ipValidationResult.Error);
            }

            var clientIp = ipValidationResult.Value;
            Console.WriteLine($"Is {clientIp} a local or public IP address?");
            Console.WriteLine("1. Local");
            Console.WriteLine("2. Public/External");
            input = Console.ReadLine();

            var ipTypeValidationResult = ValidateNumberIsWithinRange(input, 1, 2);
            if (ipTypeValidationResult.Failure)
            {
                return Result.Fail<RemoteServer>(ipTypeValidationResult.Error);
            }

            switch (ipTypeValidationResult.Value)
            {
                case PublicIpAddress:
                    remoteServerInfo.ConnectionInfo.PublicIpAddress = clientIp;
                    break;

                case LocalIpAddress:
                    remoteServerInfo.ConnectionInfo.LocalIpAddress = clientIp;
                    break;
            }

            remoteServerInfo.ConnectionInfo.Port =
                GetPortNumberFromUser("Enter the server's port number that handles incoming requests", false);

            return Result.Ok(remoteServerInfo);
        }

        public static int GetPortNumberFromUser(string prompt, bool allowRandom)
        {
            var portNumber = 0;
            while (portNumber is 0)
            {
                string input;
                Console.WriteLine($"{prompt} (range {PortRangeMin}-{PortRangeMax}):");

                if (allowRandom)
                {
                    Console.WriteLine("Enter zero to use a random port number");
                }

                input = Console.ReadLine();

                if (string.IsNullOrEmpty(input))
                {
                    Console.WriteLine("User input was null or empty string, please try again.");
                    continue;
                }

                if (input.Equals("zero") || input.Equals("0"))
                {
                    if (!allowRandom)
                    {
                        Console.WriteLine("Zero is not within the allowed range, please try again.");
                        continue;
                    }

                    var rnd = new Random();
                    portNumber = rnd.Next(PortRangeMin, PortRangeMax + 1);
                    Console.WriteLine($"Your randomly chosen port number is: {portNumber}");
                    break;
                }

                var portValidationResult = ValidateNumberIsWithinRange(input, PortRangeMin, PortRangeMax);
                if (portValidationResult.Failure)
                {
                    Console.WriteLine(portValidationResult.Error);
                    continue;
                }

                portNumber = portValidationResult.Value;
            }

            return portNumber;
        }

        private static int ChoosePublicOrLocalIpAddress(RemoteServer remoteServer, string userPrompt)
        {
            var ipChoice = 0;
            while (ipChoice == 0)
            {
                Console.WriteLine(userPrompt);
                Console.WriteLine($"1. Local IP ({remoteServer.ConnectionInfo.LocalIpAddress})");
                Console.WriteLine($"2. Public IP ({remoteServer.ConnectionInfo.PublicIpAddress})");
                
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

        private static async Task<Result> SendTextMessageToClientAsync(TplSocketServer server, ConnectionInfo myInfo, ConnectionInfo clientInfo, int ipChoice, CancellationToken token)
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
            ConnectionInfo myInfo,
            ConnectionInfo clientInfo,
            string transferFolderPath,
            int ipChoice,
            CancellationToken token)
        {
            var selectFileResult = ChooseFileToSend(transferFolderPath);
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
                    transferFolderPath, 
                    token);

            return sendFileResult.Failure ? sendFileResult : Result.Ok();
        }

        private static Result<string> ChooseFileToSend(string transferFolderPath)
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

            var fileMenuChoice = 0;
            var totalMenuChoices = listOfFiles.Count + 1;
            var returnToPreviousMenu = totalMenuChoices;

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

        private static async Task<Result> RequestFileListFromClientAsync(
            AppSettings settings,
            ConnectionInfo myInfo,
            string clientIpAddress,
            int clientPort)
        {
            var getFileFromClient = new GetFileFromClient();
            var getFileResult =
                await getFileFromClient.RunAsync(
                    settings, 
                    myInfo, 
                    clientIpAddress, 
                    clientPort);            

            if (getFileResult.Failure)
            {
                return getFileResult;
            }

            return Result.Ok();
        }
        
        private static bool UserWantsToShutdownServer()
        {
            var shutdownChoice = 0;
            while (shutdownChoice is 0)
            {
                Console.WriteLine("Shutdown server?");
                Console.WriteLine("1. Yes");
                Console.WriteLine("2. No");

                var input = Console.ReadLine();
                var validationResult = ValidateNumberIsWithinRange(input, 1, 2);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                shutdownChoice = validationResult.Value;
            }

            return shutdownChoice == 1;
        }

        private static string GetChosenIpAddress(ConnectionInfo conectionInfo, int ipChoice)
        {
            var ip = string.Empty;
            if (ipChoice == PublicIpAddress)
            {
                ip = conectionInfo.PublicIpAddress;
            }

            if (ipChoice == LocalIpAddress)
            {
                ip = conectionInfo.LocalIpAddress;
            }

            return ip;
        }

        public static Result<string> ValidateIpV4Address(string input)
        {
            var parseIpResult = IpAddressHelper.ParseSingleIPv4Address(input);
            if (parseIpResult.Failure)
            {
                return Result.Fail<string>($"Unable tp parse IPv4 address from input string: {parseIpResult.Error}");
            }

            return Result.Ok(parseIpResult.Value.ToString());
        }

        public static Result<int> ValidateNumberIsWithinRange(string input, int rangeMin, int rangeMax)
        {
            if (string.IsNullOrEmpty(input))
            {
                return Result.Fail<int>("Error! Input was null or empty string.");
            }

            if (!int.TryParse(input, out var parsedNum))
            {
                return Result.Fail<int>($"Unable to parse int value from input string: {input}");
            }

            if (parsedNum < rangeMin || parsedNum > rangeMax)
            {
                return Result.Fail<int>($"{parsedNum} is not within allowed range {rangeMin}-{rangeMax}");
            }

            return Result.Ok(parsedNum);
        }

        private static void SaveSettingsToFile(AppSettings settings)
        {
            AppSettings.Serialize(settings, $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SettingsFileName}");
        }

        private static void HandleServerEvent(ServerEventInfo serverEvent)
        {
            switch (serverEvent.EventType)
            {
                // TODO: Add new logic that adds entry to connection list ands saves to file wheneever an applicable event is handled
                // TODO: Add new logic that asks user to respond to message
                case ServerEventType.ReceiveTextMessageCompleted:
                    Console.WriteLine($"\nMessage received from client {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}:");
                    Console.WriteLine($"{serverEvent.TextMessage}\n");
                    WriteMenuToScreen();
                    break;

                case ServerEventType.ReceiveOutboundFileTransferInfoCompleted:
                    Console.WriteLine("\nReceived Outbound File Transfer Request");
                    Console.WriteLine($"\tFile Requested:\t\t{serverEvent.FileName}\n\tFile Size:\t\t{serverEvent.FileSizeString}\n\tRemote Endpoint:\t{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n\tTarget Directory:\t{serverEvent.RemoteFolder}\n");
                    WriteMenuToScreen();
                    break;

                case ServerEventType.ReceiveInboundFileTransferInfoCompleted:
                    Console.WriteLine("\nReceived Inbound File Transfer Request");
                    Console.WriteLine($"\tFile Name:\t\t\t{serverEvent.FileName}\n\tFile Size:\t\t\t{serverEvent.FileSizeString}\n");
                    WriteMenuToScreen();
                    break;

                case ServerEventType.SendFileBytesStarted:
                    Console.WriteLine("\nSending file to client...");
                    break;

                case ServerEventType.ReceiveFileBytesStarted:
                    Console.WriteLine("\nReceving file from client...");
                    break;

                case ServerEventType.FileTransferProgress:
                    Console.WriteLine($"\nFile Transfer {serverEvent.PercentComplete:P0} Complete\n");
                    break;

                case ServerEventType.ReceiveConfirmationMessageCompleted:
                    Console.WriteLine("Client confirmed file transfer completed successfully\n");
                    WriteMenuToScreen();
                    break;

                case ServerEventType.ReceiveFileBytesCompleted:
                    Console.WriteLine("\nSuccessfully received file from client");
                    Console.WriteLine($"\tTransfer Start Time:\t{serverEvent.FileTransferStartTime.ToLongTimeString()}\n\tTransfer Complete Time:\t{serverEvent.FileTransferCompleteTime.ToLongTimeString()}\n\tElapsed Time:\t\t\t{serverEvent.FileTransferElapsedTimeString}\n\tTransfer Rate:\t\t\t{serverEvent.FileTransferRate}\n");
                    WriteMenuToScreen();
                    break;

                case ServerEventType.SendFileListRequestStarted:
                    Console.WriteLine($"\nSending request for list of downloadable files to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n");
                    break;

                case ServerEventType.ReceiveFileListRequestCompleted:
                    Console.WriteLine($"\nReceived request for list of downloadable files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
                    break;

                case ServerEventType.SendFileListResponseStarted:
                    Console.WriteLine($"Sending list of downloadable files to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.FileInfoList.Count} files in list)");
                    break;

                case ServerEventType.ReceiveFileListResponseCompleted:
                    Console.WriteLine($"\nReceived list of downloadable files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.FileInfoList.Count} files in list)\n");
                    break;

                case ServerEventType.SendPublicIpRequestStarted:
                    Console.WriteLine($"\nSending request for public IP address to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n");
                    break;

                case ServerEventType.ReceivePublicIpRequestCompleted:
                    Console.WriteLine($"\nReceived request for public IP address from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
                    break;

                case ServerEventType.SendPublicIpResponseStarted:
                    Console.WriteLine($"Sending public IP address to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.PublicIpAddress})");
                    break;

                case ServerEventType.ReceivePublicIpResponseCompleted:
                    Console.WriteLine($"\nReceived public IP address from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.PublicIpAddress})\n");                    
                    break;

                case ServerEventType.SendTransferFolderRequestStarted:
                    Console.WriteLine($"\nSending request for transfer folder path to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n");
                    break;

                case ServerEventType.ReceiveTransferFolderRequestCompleted:
                    Console.WriteLine($"\nReceived request for transfer folder path from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
                    break;

                case ServerEventType.SendTransferFolderResponseStarted:
                    Console.WriteLine($"Sending transfer folder path to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.LocalFolder})");
                    break;

                case ServerEventType.ReceiveTransferFolderResponseCompleted:
                    Console.WriteLine($"\nReceived transfer folder path from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.RemoteFolder})\n");
                    break;

                case ServerEventType.ShutdownListenSocketCompleted:
                    Console.WriteLine("\nServer has been successfully shutdown\n");
                    break;

                case ServerEventType.ErrorOccurred:
                    Console.WriteLine($"Error occurred: {serverEvent.ErrorMessage}");
                    break;
            }
        }
    }
}
