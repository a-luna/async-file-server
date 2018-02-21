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

        const int SendTextMessage = 1;
        const int SendFile = 2;
        const int GetFile = 3;
        const int ShutDown = 4;

        const int LocalIpAddress = 1;
        const int PublicIpAddress = 2;        

        const int PortRangeMin = 49152;
        const int PortRangeMax = 65535;

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

                await ServerMenu(listenServer, myInfo, settings, token);

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
                Console.WriteLine($"{ex.Message} ({ex.GetType()}) raised in method Program.Main");
            }
            finally
            {
                listenServer.CloseListenSocket();
            }

            Console.WriteLine("Press enter to exit.");
            Console.ReadLine();
        }

        private static AppSettings InitializeAppSettings()
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
                GetPortNumberFromUser("Enter a port number where this server will listen for incoming connections");

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

        private static async Task<Result> ServerMenu(TplSocketServer server, ConnectionInfo myInfo, AppSettings settings, CancellationToken token)
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
                var ipChoice = ChoosePublicOrLocalIpAddress(clientInfo, IpChoiceClient);
                var clientIpAddress = GetChosenIpAddress(clientInfo, ipChoice);

                var result = Result.Ok();
                switch (menuChoice)
                {   
                    case SendTextMessage:
                        result = await SendTextMessageToClientAsync(
                                        server, 
                                        myInfo, 
                                        clientInfo, 
                                        ipChoice, 
                                        token);
                        break;

                    case SendFile:
                        result = await SendFileToClientAsync(
                                        server, 
                                        myInfo, 
                                        clientInfo, 
                                        settings.TransferFolderPath, 
                                        ipChoice, 
                                        token);
                        break;

                    case GetFile:
                        result = await GetFileFromClientAsync(
                                        settings, 
                                        clientIpAddress,
                                        clientInfo.Port,
                                        myInfo.LocalIpAddress, 
                                        myInfo.Port,
                                        settings.TransferFolderPath,
                                        token);
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
        
        private static Result<ConnectionInfo> ChooseClient(AppSettings settings)
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
                    Console.WriteLine($"{i + 1}. Local IP: [{thisClient.GetLocalEndPoint()}]\tPublic IP: [{thisClient.GetPublicEndPoint()}]");
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
                return Result.Fail<ConnectionInfo>(string.Empty);
            }

            if (clientMenuChoice == addNewClient)
            {
                return AddNewClient(settings);
            }
            
            return Result.Ok(settings.RemoteServers[clientMenuChoice - 1]);
        }

        private static Result<ConnectionInfo> AddNewClient(AppSettings settings)
        {
            var newClientInfo = new ConnectionInfo();
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

            SaveSettingsToFile(settings);
            return Result.Ok(newClientInfo);
        }

        private static Result<ConnectionInfo> GetNewClientInfoFromUser()
        {
            var clientInfo = new ConnectionInfo();
            Console.WriteLine("Enter the server's IPv4 address:");
            var input = Console.ReadLine();

            var ipValidationResult = ValidateIpV4Address(input);
            if (ipValidationResult.Failure)
            {
                return Result.Fail<ConnectionInfo>(ipValidationResult.Error);
            }

            var clientIp = ipValidationResult.Value;
            Console.WriteLine($"Is {clientIp} a local or public IP address?");
            Console.WriteLine("1. Public/External");
            Console.WriteLine("2. Local");
            input = Console.ReadLine();

            var ipTypeValidationResult = ValidateNumberIsWithinRange(input, 1, 2);
            if (ipTypeValidationResult.Failure)
            {
                return Result.Fail<ConnectionInfo>(ipTypeValidationResult.Error);
            }

            switch (ipTypeValidationResult.Value)
            {
                case PublicIpAddress:
                    clientInfo.PublicIpAddress = clientIp;
                    break;

                case LocalIpAddress:
                    clientInfo.LocalIpAddress = clientIp;
                    break;
            }

            clientInfo.Port = GetPortNumberFromUser("Enter the server's port number that handles incoming requests");

            Console.WriteLine("Thank you! This server has been successfully configured.");
            return Result.Ok(clientInfo);
            // TODO: Add section here to get transferfolder path from server directory with new request type

            return Result.Ok(clientInfo);
        }

        private static int GetPortNumberFromUser(string prompt)
        {
            var portNumber = 0;
            while (portNumber is 0)
            {
                Console.WriteLine($"{prompt} (range {PortRangeMin}-{PortRangeMax}):");
                Console.WriteLine("Enter zero to use a random port number");
                var input = Console.ReadLine();

                if (string.IsNullOrEmpty(input))
                {
                    continue;
                }

                if (input.Equals("zero") || input.Equals("0"))
                {
                    var rnd = new Random();
                    portNumber = rnd.Next(PortRangeMin, PortRangeMax + 1);
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

        private static int ChoosePublicOrLocalIpAddress(ConnectionInfo conectionInfo, string userPrompt)
        {
            var ipChoice = 0;
            while (ipChoice == 0)
            {
                Console.WriteLine(userPrompt);
                Console.WriteLine($"1. Local IP ({conectionInfo.LocalIpAddress})");
                Console.WriteLine($"2. Public IP ({conectionInfo.PublicIpAddress})");
                
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

        private static async Task<Result> GetFileFromClientAsync(
            AppSettings settings,
            string clientIpAddress,
            int clientPort,
            string localIpAddress,
            int localPort,
            string targetFolder,
            CancellationToken token)
        {
            var server = new TplSocketServer(settings);

            Console.WriteLine($"Requesting list of files from {clientIpAddress}:{clientPort}...");
            Console.WriteLine(string.Empty);

            var requestFileListResult =
                 await server.RequestFileListAsync(
                     clientIpAddress,
                     clientPort,
                     localIpAddress,
                     localPort,
                     targetFolder,
                     token);

            return requestFileListResult.Failure ? requestFileListResult : Result.Ok();
        }

        private static async Task<Result> DownloadFileFromClient(
            List<(string filePath, long fileSizeInBytes)> fileInfoList,
            string remoteIp, 
            int remotePort,
            string localIp,
            int localPort,
            string localFolder
            )
        {
            var selectFileResult = ChooseFileToGet(fileInfoList);
            if (selectFileResult.Failure)
            {
                Console.WriteLine(selectFileResult.Error);
                return Result.Ok();
            }

            var fileToGet = selectFileResult.Value;

            var settings = InitializeAppSettings();
            var transferServer = new TplSocketServer(settings);

            var getFileResult =
                await transferServer.GetFileAsync(
                    remoteIp,
                    remotePort,
                    fileToGet,
                    localIp,
                    localPort,
                    localFolder,
                    new CancellationToken(false));

            return getFileResult.Failure ? getFileResult : Result.Ok();
        }

        private static Result<string> ChooseFileToGet(List<(string filePath, long fileSizeInBytes)> fileInfoList)
        {
            var fileMenuChoice = 0;
            var totalMenuChoices = fileInfoList.Count + 1;
            var returnToPreviousMenu = totalMenuChoices;

            while (fileMenuChoice == 0)
            {
                Console.WriteLine("Choose a file to download:");

                foreach (var i in Enumerable.Range(0, fileInfoList.Count))
                {
                    var fileName = Path.GetFileName(fileInfoList[i].filePath);
                    Console.WriteLine($"{i + 1}. {fileName} ({fileInfoList[i].fileSizeInBytes.ConvertBytesForDisplay()})");
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

            return fileMenuChoice == returnToPreviousMenu 
                ? Result.Fail<string>("Returning to main menu") 
                : Result.Ok(fileInfoList[fileMenuChoice - 1].filePath);
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

        private static Result<string> ValidateIpV4Address(string input)
        {
            var parseIpResult = IpAddressHelper.ParseSingleIPv4Address(input);
            if (parseIpResult.Failure)
            {
                return Result.Fail<string>($"Unable tp parse IPv4 address from input string: {parseIpResult.Error}");
            }

            return Result.Ok(parseIpResult.Value.ToString());
        }

        private static Result<int> ValidateNumberIsWithinRange(string input, int rangeMin, int rangeMax)
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

        private static async void HandleServerEvent(ServerEventInfo serverEvent)
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
                    Console.WriteLine("\nSending file to client...");
                    break;

                case ServerEventType.ReceiveFileBytesStarted:
                    Console.WriteLine("\nReceving file from client...");
                    break;

                case ServerEventType.FileTransferProgress:
                    Console.WriteLine($"\nFile Transfer {serverEvent.PercentComplete:P0} Complete\n");
                    break;

                case ServerEventType.ReceiveConfirmationMessageCompleted:
                    Console.WriteLine("\nClient confirmed file transfer completed successfully\n");
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
                    Console.WriteLine($"\nReceived request for list of downloadable files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n");
                    break;

                case ServerEventType.SendFileListResponseStarted:
                    Console.WriteLine($"\nSending list of downloadable files to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.FileInfoList.Count} files in list)\n");
                    break;

                case ServerEventType.ReceiveFileListResponseCompleted:
                    Console.WriteLine($"\nReceived list of downloadable files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.FileInfoList.Count} files in list)\n");
                    await DownloadFileFromClient(
                        serverEvent.FileInfoList,
                        serverEvent.RemoteServerIpAddress,
                        serverEvent.RemoteServerPortNumber,
                        serverEvent.LocalIpAddress,
                        serverEvent.LocalPortNumber,
                        serverEvent.LocalFolder
                        );
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
