namespace ServerConsole
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using AaronLuna.Common.Console;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Numeric;
    using AaronLuna.Common.Result;

    using TplSocketServer;

    // TODO: Add new logic that adds entry to connection list ands saves to file wheneever an applicable event is handled
    // TODO: Add new logic that asks user to respond to message

    public class ServerProgram
    {
        const string SettingsFileName = "settings.xml";
        const string DefaultTransferFolderName = "transfer";

        const string IpChoiceClient = "Which IP address would you like to use for this request?";
        const string NotifyLanTrafficOnly = "Unable to determine public IP address, this server will only be able to communicate with machines in the same local network.";
        const string PropmptMultipleLocalIPv4Addresses = "There are multiple IPv4 addresses available on this machine, choose the most appropriate local address:";
        const string EmptyTransferFolderErrorMessage = "Currently there are no files available in transfer folder";
        const string FileAlreadyExists = "A file with the same name already exists in the download folder, please rename or remove this file in order to proceed.";
        
        const int SendMessage = 1;
        const int SendFile = 2;
        const int GetFile = 3;
        const int ShutDown = 4;

        const int LocalIpAddress = 1;
        const int PublicIpAddress = 2;

        const int ReplyToTextMessage = 1;
        const int EndTextSession = 2;

        const int PortRangeMin = 49152;
        const int PortRangeMax = 65535;

        readonly string _settingsFilePath;
        readonly string _transferFolderPath;

        bool _waitingForServerToBeginAcceptingConnections = true;
        bool _waitingForTransferFolderResponse = true;
        bool _waitingForPublicIpResponse = true;
        bool _waitingForFileListResponse = true;
        bool _waitingForDownloadToComplete = true;
        bool _waitingForConfirmationMessage = true;
        bool _activeTextSession;
        bool _requestedFileFromClient;
        bool _fileTransferInProgress;
        bool _needToRewriteMenu;
        bool _errorOccurred;

        string _clientTransferFolderPath;
        IPAddress _clientPublicIp;
        List<(string filePath, long fileSize)> _fileInfoList;
        
        readonly CancellationTokenSource _cts;
        CancellationToken _token;
        ConsoleProgressBar _progress;

        AppSettings _settings;
        ConnectionInfo _myInfo;
        TplSocketServer _server;

        string _textSessionIp;
        int _textSessionPort;

        public ServerProgram()
        {
            _server = new TplSocketServer();
            _cts = new CancellationTokenSource();
            
            _settingsFilePath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SettingsFileName}";
            _transferFolderPath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{DefaultTransferFolderName}";
        }

        public event ServerEventDelegate EventOccurred;

        public async Task RunAsyncServer()
        {
            _token = _cts.Token;

            _settings = InitializeAppSettings();
            _myInfo = await GetLocalServerSettingsFromUser();

            _server = new TplSocketServer(_settings, _myInfo.LocalIpAddress);
            _server.EventOccurred += HandleServerEvent;

            _waitingForServerToBeginAcceptingConnections = true;

            try
            {
                var listenTask =
                    Task.Run(() => 
                        _server.HandleIncomingConnectionsAsync(_myInfo.Port, _token), 
                        _token);

                while (_waitingForServerToBeginAcceptingConnections) { }
                
                await ServerMenuAsync();

                if (_fileTransferInProgress)
                {
                    _progress.Dispose();
                }

                _cts.Cancel();
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
                _server.CloseListenSocket();
            }
        }

        private AppSettings InitializeAppSettings()
        {
            var settings = new AppSettings
            {
                TransferFolderPath = _transferFolderPath
            };
            
            if (!File.Exists(_settingsFilePath)) return settings;

            var deserialized = AppSettings.Deserialize(_settingsFilePath);
            if (deserialized.Success)
            {
                settings = deserialized.Value;
            }
            else
            {
                Console.WriteLine(deserialized.Error);
            }

            return settings;
        }

        private async Task<ConnectionInfo> GetLocalServerSettingsFromUser()
        {
            var portChoice =
                GetPortNumberFromUser("Enter a port number where this server will listen for incoming connections", true);

            var localIp = GetLocalIpToBindTo();
            var publicIp = IPAddress.None;

            var retrievePublicIp = await IpAddressHelper.GetPublicIPv4AddressAsync();
            if (retrievePublicIp.Failure)
            {
                Console.WriteLine(Environment.NewLine);
                Console.WriteLine(NotifyLanTrafficOnly);
            }
            else
            {
                publicIp = retrievePublicIp.Value;
            }

            return new ConnectionInfo
            {
                LocalIpAddress = localIp,
                PublicIpAddress = publicIp,
                Port = portChoice
            };
        }
        
        private int GetPortNumberFromUser(string prompt, bool allowRandom)
        {
            var portNumber = 0;
            while (portNumber is 0)
            {
                Console.WriteLine($"{prompt} (range {PortRangeMin}-{PortRangeMax}):");

                if (allowRandom)
                {
                    Console.WriteLine("Enter zero to use a random port number");
                }

                var input = Console.ReadLine();

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

        private IPAddress GetLocalIpToBindTo()
        {
            var localIps = IpAddressHelper.GetLocalIPv4AddressList();
            if (localIps.Count == 1)
            {
                return localIps[0];
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

            return localIps[ipChoice - 1];
        }

        private async Task<Result> ServerMenuAsync()
        {   
            while (true)
            {
                Console.WriteLine($"\nServer is listening for incoming requests on port {_myInfo.Port}\nLocal IP: {_myInfo.LocalIpString}\nPublic IP: {_myInfo.PublicIpString}");

                var menuResult = await GetMenuChoiceAsync();
                if (menuResult.Failure)
                {
                    Console.WriteLine(menuResult.Error);
                    continue;
                }

                var menuChoice = menuResult.Value;

                if (_activeTextSession)
                {
                    _activeTextSession = false;
                    continue;
                }

                if (menuChoice == ShutDown)
                {
                    Console.WriteLine("Server is shutting down");
                    break;
                }

                var chooseClientResult = await ChooseClientAsync();
                if (chooseClientResult.Failure)
                {
                    Console.WriteLine(chooseClientResult.Error);
                    continue;
                }

                var clientInfo = chooseClientResult.Value;
                var ipChoice = ChoosePublicOrLocalIpAddress(clientInfo, IpChoiceClient);
                var clientIpAddress = GetChosenIpAddress(clientInfo.ConnectionInfo, ipChoice);
                var clientPort = clientInfo.ConnectionInfo.Port;

                var result = Result.Ok();
                switch (menuChoice)
                {
                    case SendMessage:
                        result = await SendTextMessageToClientAsync(clientIpAddress.ToString(), clientPort, _token);
                        break;

                    case SendFile:
                        result = await SendFileToClientAsync(clientInfo, clientIpAddress.ToString(), clientPort, _token);
                        break;

                    case GetFile:
                        result = await RequestFileListFromClientAsync(clientIpAddress.ToString(), clientPort, _token);
                        break;
                }

                if (result.Success)
                {
                    Console.WriteLine("Returning to main menu...");
                    continue;
                }

                Console.WriteLine(result.Error);
                if (UserWantsToShutdownServer())
                {
                    return result;
                }
            }

            return Result.Ok();
        }

        private async Task<Result<int>> GetMenuChoiceAsync()
        {
            await WriteMenuToScreenAsync(false);
            _needToRewriteMenu = true;

            var input = Console.ReadLine();
            Console.WriteLine(string.Empty);

            if (_activeTextSession)
            {
                await PromptUserToReplyToTextMessageAsync();
                return Result.Ok(0);
            }

            var validationResult = ValidateNumberIsWithinRange(input, 1, 4);
            if (validationResult.Failure)
            {
                return validationResult;
            }

            return Result.Ok(validationResult.Value);
        }

        private async Task<Result> PromptUserToReplyToTextMessageAsync()
        {
            var userMenuChoice = 0;
            while (userMenuChoice is 0)
            {
                Console.WriteLine($"Reply to {_textSessionIp}:{_textSessionPort}?");
                Console.WriteLine("1. Yes");
                Console.WriteLine("2. No");
                var input = Console.ReadLine();
                Console.WriteLine(string.Empty);

                var validationResult = ValidateNumberIsWithinRange(input, 1, 2);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                userMenuChoice = validationResult.Value;
            }

            switch (userMenuChoice)
            {
                case ReplyToTextMessage:
                    await SendTextMessageToClientAsync(_textSessionIp, _textSessionPort, new CancellationToken());
                    break;

                case EndTextSession:
                    _activeTextSession = false;
                    break;
            }

            return Result.Ok();
        }

        private async Task WriteMenuToScreenAsync(bool waitingForUserInput)
        {
            if (_activeTextSession)
            {
                await Task.Run(() => SendEnterToConsole.Execute(), _token);                
                return;
            }

            if (waitingForUserInput)
            {
                Console.WriteLine("Returning to main menu...");
                Console.WriteLine($"\nServer is listening for incoming requests on port {_myInfo.Port}\nLocal IP:\t{_myInfo.LocalIpString}\nPublic IP:\t{_myInfo.PublicIpString}");
            }

            Console.WriteLine("\nPlease make a choice from the menu below:");
            Console.WriteLine("1. Send Text Message");
            Console.WriteLine("2. Send File");
            Console.WriteLine("3. Get File");
            Console.WriteLine("4. Shutdown");            
        }

        private async Task<Result<RemoteServer>> ChooseClientAsync()
        {
            var clientMenuChoice = 0;
            var totalMenuChoices = _settings.RemoteServers.Count + 2;
            var addNewClient = _settings.RemoteServers.Count + 1;
            var returnToMainMenu = totalMenuChoices;

            while (clientMenuChoice == 0)
            {
                Console.WriteLine("Choose a remote server for this request:");

                foreach (var i in Enumerable.Range(0, _settings.RemoteServers.Count))
                {
                    var thisClient = _settings.RemoteServers[i];
                    Console.WriteLine($"\n{i + 1}.\tLocal IP: {thisClient.ConnectionInfo.LocalIpString}\n\tPublic IP: {thisClient.ConnectionInfo.PublicIpString}\n\tPort: {thisClient.ConnectionInfo.Port}");
                }

                Console.WriteLine($"\n{addNewClient}. Add New Client");
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
                return await AddNewClientAsync();
            }

            return Result.Ok(_settings.RemoteServers[clientMenuChoice - 1]);
        }

        private async Task<Result<RemoteServer>> AddNewClientAsync()
        {
            var newClient = new RemoteServer();
            var clientInfoIsValid = false;

            while (!clientInfoIsValid)
            {
                var addClientResult = GetRemoteServerConnectionInfoFromUser();
                if (addClientResult.Failure)
                {
                    return addClientResult;
                }

                newClient = addClientResult.Value;
                clientInfoIsValid = true;
            }

            var clientIp = IPAddress.None;
            var localIp = newClient.ConnectionInfo.LocalIpAddress;
            var publicIp = newClient.ConnectionInfo.PublicIpAddress;

            if (!Equals(localIp, IPAddress.None))
            {
                clientIp = localIp;
            }

            if (Equals(localIp, IPAddress.None) && !Equals(publicIp, IPAddress.None))
            {
                clientIp = publicIp;
            }

            if (Equals(clientIp, IPAddress.None))
            {
                return Result.Fail<RemoteServer>("There was an error getting the client's IP address from user input.");
            }           

            return await RequestAdditionalConnectionInfoFromClient(clientIp, newClient);
        }

        public Result<RemoteServer> GetRemoteServerConnectionInfoFromUser()
        {
            var remoteServerInfo = new RemoteServer();

            Console.WriteLine("Enter the server's IPv4 address:");
            var input = Console.ReadLine();

            var ipValidationResult = ValidateIpV4Address(input);
            if (ipValidationResult.Failure)
            {
                return Result.Fail<RemoteServer>(ipValidationResult.Error);
            }

            var parseIp = IpAddressHelper.ParseSingleIPv4Address(ipValidationResult.Value);
            var clientIp = parseIp.Value;

            var userInputIsValid = false;
            int userMenuChoice = 0;
            while (!userInputIsValid)
            {
                Console.WriteLine($"\nIs {clientIp} a local or public IP address?");
                Console.WriteLine("1. Local");
                Console.WriteLine("2. Public/External");
                input = Console.ReadLine();

                var ipTypeValidationResult = ValidateNumberIsWithinRange(input, 1, 2);
                if (ipTypeValidationResult.Failure)
                {
                    Console.WriteLine(ipTypeValidationResult.Error);
                    continue;
                }

                userInputIsValid = true;
                userMenuChoice = ipTypeValidationResult.Value;
            }           

            switch (userMenuChoice)
            {
                case PublicIpAddress:
                    remoteServerInfo.ConnectionInfo.PublicIpAddress = clientIp;
                    break;

                case LocalIpAddress:
                    remoteServerInfo.ConnectionInfo.LocalIpAddress = clientIp;
                    break;
            }

            remoteServerInfo.ConnectionInfo.Port =
                GetPortNumberFromUser("\nEnter the server's port number that handles incoming requests", false);

            return Result.Ok(remoteServerInfo);
        }

        private async Task<Result<RemoteServer>> RequestAdditionalConnectionInfoFromClient(IPAddress clientIp, RemoteServer client)
        {
            var clientPort = client.ConnectionInfo.Port;

            if (client.ConnectionInfo.IsEqualTo(_myInfo))
            {
                return Result.Fail<RemoteServer>($"{clientIp}:{clientPort} is the same IP address and port number used by this server, returning to main menu.");
            }

            if (ClientAlreadyExists(client))
            {
                return Result.Fail<RemoteServer>(
                    "A client with the same IP address and Port # has already been added.");
            }

            Console.WriteLine("\nRequesting additional information from client...\n");

            _waitingForTransferFolderResponse = true;
            _clientTransferFolderPath = string.Empty;

            var sendFolderRequestResult =
                await _server.RequestTransferFolderPath(
                        clientIp.ToString(),
                        clientPort,
                        _myInfo.LocalIpAddress.ToString(),
                        _myInfo.Port,
                        _token)
                        .ConfigureAwait(false);

            if (sendFolderRequestResult.Failure)
            {
                return Result.Fail<RemoteServer>(
                    $"Error requesting transfer folder path from new client:\n{sendFolderRequestResult.Error}");
            }

            while (_waitingForTransferFolderResponse) { }
            client.TransferFolder = _clientTransferFolderPath;

            if (Equals(client.ConnectionInfo.PublicIpAddress, IPAddress.None))
            {
                _waitingForPublicIpResponse = true;
                _clientPublicIp = IPAddress.None;

                var sendIpRequestResult =
                    await _server.RequestPublicIp(
                            clientIp.ToString(),
                            clientPort,
                            _myInfo.LocalIpAddress.ToString(),
                            _myInfo.Port,
                            _token)
                        .ConfigureAwait(false);

                if (sendIpRequestResult.Failure)
                {
                    return Result.Fail<RemoteServer>(
                        $"Error requesting transfer folder path from new client:\n{sendIpRequestResult.Error}");
                }

                while (_waitingForPublicIpResponse) { }
                client.ConnectionInfo.PublicIpAddress = _clientPublicIp;
            }

            Console.WriteLine("Thank you! Connection info for client has been successfully configured.\n");

            _settings.RemoteServers.Add(client);
            SaveSettingsToFile(_settings);
            return Result.Ok(client);
        }
        
        private bool ClientAlreadyExists(RemoteServer newClient)
        {
            var clients = _settings.RemoteServers;
            var exists = false;
            foreach (var remoteServer in clients)
            {
                if (remoteServer.ConnectionInfo.IsEqualTo(newClient.ConnectionInfo))
                {
                    exists = true;
                    break;
                }
            }
            return exists;
        }
        
        private void SaveSettingsToFile(AppSettings settings)
        {
            AppSettings.Serialize(settings, $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SettingsFileName}");
        }

        private int ChoosePublicOrLocalIpAddress(RemoteServer remoteServer, string userPrompt)
        {
            var ipChoice = 0;
            while (ipChoice == 0)
            {
                Console.WriteLine(userPrompt);
                Console.WriteLine($"1. Local IP ({remoteServer.ConnectionInfo.LocalIpString})");
                Console.WriteLine($"2. Public IP ({remoteServer.ConnectionInfo.PublicIpString})");

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

        private async Task<Result> SendTextMessageToClientAsync(string ipAddress, int port, CancellationToken token)
        {
            Console.WriteLine($"Please enter a text message to send to {ipAddress}:{port}");
            var message = Console.ReadLine();
            
            var sendMessageResult =
                await _server.SendTextMessageAsync(
                    message,
                    ipAddress,
                    port,
                    _myInfo.LocalIpAddress.ToString(),
                    _myInfo.Port,
                    token);

            return sendMessageResult.Failure ? sendMessageResult : Result.Ok();
        }

        private async Task<Result> SendFileToClientAsync(RemoteServer clientInfo, string ipAddress, int port, CancellationToken token)
        {
            var selectFileResult = ChooseFileToSend(_transferFolderPath);
            if (selectFileResult.Failure)
            {
                Console.WriteLine(selectFileResult.Error);
                return Result.Ok();
            }

            var fileToSend = selectFileResult.Value;
            _waitingForConfirmationMessage = true;
            _errorOccurred = false;

            var sendFileTask = 
                Task.Run(() => 
                    _server.SendFileAsync(
                        ipAddress,
                        port,
                        fileToSend,
                        clientInfo.TransferFolder,
                    token));

            while (_waitingForConfirmationMessage) { }

            var sendFileResult = await sendFileTask;
            if (_errorOccurred)
            {
                return Result.Fail(
                    "Client rejected the transfer, file with same name and size has already been downloaded,");
            }

            return sendFileResult.Failure ? sendFileResult : Result.Ok();
        }

        private Result<string> ChooseFileToSend(string transferFolderPath)
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
                    $"Transfer folder is empty, please place files in the path below:\n{transferFolderPath}\n\nReturning to main menu...");
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

        private async Task<Result> RequestFileListFromClientAsync(string ipAddress, int port, CancellationToken token)
        {
            _waitingForFileListResponse = true;
            _waitingForDownloadToComplete = true;
            _errorOccurred = false;

            var requestFileListResult =
                await _server.RequestFileListAsync(
                        ipAddress,
                        port,
                        _myInfo.LocalIpAddress.ToString(),
                        _myInfo.Port,
                        _transferFolderPath,
                        token)
                    .ConfigureAwait(false);

            if (requestFileListResult.Failure)
            {
                return Result.Fail(
                    $"Error requesting list of available files from client:\n{requestFileListResult.Error}");
            }

            while (_waitingForFileListResponse) { }

            if (_errorOccurred)
            {
                return Result.Fail("Client has no files available to download.");
            }

            var fileDownloadResult = await DownloadFileFromClient(ipAddress, port).ConfigureAwait(false);
            if (fileDownloadResult.Failure)
            {
                return fileDownloadResult;
            }
            
            while (_waitingForDownloadToComplete) { }
            
            return Result.Ok();
        }

        private async Task<Result> DownloadFileFromClient(string remoteIp, int remotePort)
        {
            var selectFileResult = ChooseFileToGet();
            if (selectFileResult.Failure)
            {
                Console.WriteLine(selectFileResult.Error);
                return Result.Fail(selectFileResult.Error);
            }

            var fileToGet = selectFileResult.Value;
            _needToRewriteMenu = false;
            _requestedFileFromClient = true;
            
            var getFileResult =
                await _server.GetFileAsync(
                    remoteIp,
                    remotePort,
                    fileToGet,
                    _myInfo.LocalIpAddress.ToString(),
                    _myInfo.Port,
                    _transferFolderPath,
                    _token)
                    .ConfigureAwait(false);

            return getFileResult.Failure ? getFileResult : Result.Ok();
        }

        private Result<string> ChooseFileToGet()
        {
            var fileMenuChoice = 0;
            var totalMenuChoices = _fileInfoList.Count + 1;
            var returnToMainMenu = totalMenuChoices;

            while (fileMenuChoice == 0)
            {
                Console.WriteLine("Choose a file to download:");

                foreach (var i in Enumerable.Range(0, _fileInfoList.Count))
                {
                    var fileName = Path.GetFileName(_fileInfoList[i].filePath);
                    Console.WriteLine($"{i + 1}. {fileName} ({_fileInfoList[i].fileSize.ConvertBytesForDisplay()})");
                }

                Console.WriteLine($"{returnToMainMenu}. Return to Main Menu");

                var input = Console.ReadLine();

                var validationResult = ValidateNumberIsWithinRange(input, 1, totalMenuChoices);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                fileMenuChoice = validationResult.Value;
            }

            return fileMenuChoice == returnToMainMenu
                ? Result.Fail<string>("Returning to main menu")
                : Result.Ok(_fileInfoList[fileMenuChoice - 1].filePath);
        }

        private bool UserWantsToShutdownServer()
        {
            var shutdownChoice = 0;
            while (shutdownChoice is 0)
            {
                Console.WriteLine("\nShutdown server?");
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

        private IPAddress GetChosenIpAddress(ConnectionInfo conectionInfo, int ipChoice)
        {
            var ip = IPAddress.None;
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

        public Result<string> ValidateIpV4Address(string input)
        {
            var parseIpResult = IpAddressHelper.ParseSingleIPv4Address(input);
            if (parseIpResult.Failure)
            {
                return Result.Fail<string>($"Unable tp parse IPv4 address from input string: {parseIpResult.Error}");
            }

            return Result.Ok(parseIpResult.Value.ToString());
        }

        private Result<int> ValidateNumberIsWithinRange(string input, int rangeMin, int rangeMax)
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

        private async void HandleServerEvent(ServerEventInfo serverEvent)
        {
            EventOccurred?.Invoke(serverEvent);

            switch (serverEvent.EventType)
            {
                case ServerEventType.AcceptConnectionAttemptStarted:
                    _waitingForServerToBeginAcceptingConnections = false;
                    break;

                case ServerEventType.ReceiveTextMessageCompleted:
                    
                    await SaveNewClientAsync(serverEvent.RemoteServerIpAddress, serverEvent.RemoteServerPortNumber);
                    _textSessionIp = serverEvent.RemoteServerIpAddress;
                    _textSessionPort = serverEvent.RemoteServerPortNumber;
                    _activeTextSession = true;

                    if (serverEvent.TextMessage.Contains(EmptyTransferFolderErrorMessage))
                    {
                        _waitingForFileListResponse = false;
                        _errorOccurred = true;
                    }
                    else if (serverEvent.TextMessage.Contains(FileAlreadyExists))
                    {
                        _waitingForConfirmationMessage = false;
                        _errorOccurred = true;
                    }
                    else
                    {
                        Console.WriteLine($"\n{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} says:");
                        Console.WriteLine($"{serverEvent.TextMessage}");
                        
                        await WriteMenuToScreenAsync(true);
                    }
                    
                    break;

                case ServerEventType.ReceiveInboundFileTransferInfoCompleted:

                    if (!_requestedFileFromClient)
                    {
                        await SaveNewClientAsync(serverEvent.RemoteServerIpAddress, serverEvent.RemoteServerPortNumber);
                    }

                    break;

                case ServerEventType.ReceiveFileBytesStarted:

                    _progress = new ConsoleProgressBar
                    {
                        NumberOfBlocks = 20,
                        StartBracketChar = '|',
                        EndBracketChar = '|',
                        CompletedBlockChar = '\u00BB',
                        UncompletedBlockChar = ' ',
                        AnimationSequence = ".oO@*"
                    };
                    _fileTransferInProgress = true;
                    break;

                case ServerEventType.FileTransferProgress:
                    _progress.Report(serverEvent.PercentComplete);
                    break;

                case ServerEventType.ReceiveFileBytesCompleted:

                    _progress.Report(1);
                    await Task.Delay(500);
                    _progress.Dispose();

                    Console.WriteLine($"\n\nTransfer Start Time:\t\t{serverEvent.FileTransferStartTime.ToLongTimeString()}");
                    Console.WriteLine($"Transfer Complete Time:\t\t{serverEvent.FileTransferCompleteTime.ToLongTimeString()}");
                    Console.WriteLine($"Elapsed Time:\t\t\t{serverEvent.FileTransferElapsedTimeString}");
                    Console.WriteLine($"Transfer Rate:\t\t\t{serverEvent.FileTransferRate}\n");

                    _waitingForDownloadToComplete = false;
                    _requestedFileFromClient = false;

                    if (_needToRewriteMenu)
                    {
                        await WriteMenuToScreenAsync(true);
                    }

                    break;

                case ServerEventType.ReceiveConfirmationMessageCompleted:
                    _waitingForConfirmationMessage = false;
                    await WriteMenuToScreenAsync(true);
                    break;

                case ServerEventType.ReceiveFileListRequestCompleted:
                    await SaveNewClientAsync(serverEvent.RemoteServerIpAddress, serverEvent.RemoteServerPortNumber);
                    break;

                case ServerEventType.ReceiveFileListResponseCompleted:
                    _waitingForFileListResponse = false;
                    _fileInfoList = serverEvent.FileInfoList;
                    break;

                case ServerEventType.ReceiveTransferFolderResponseCompleted:
                    _clientTransferFolderPath = serverEvent.RemoteFolder;
                    _waitingForTransferFolderResponse = false;
                    break;

                case ServerEventType.SendPublicIpResponseStarted:
                    await WriteMenuToScreenAsync(true);
                    break;

                case ServerEventType.ReceivePublicIpResponseCompleted:

                    var clientPublicIpString = serverEvent.PublicIpAddress;
                    var parseIp = IpAddressHelper.ParseSingleIPv4Address(clientPublicIpString);
                    _clientPublicIp = parseIp.Value;

                    _waitingForPublicIpResponse = false;
                    break;

                case ServerEventType.ErrorOccurred:

                    Console.WriteLine("Fatal error occurred, shutting down server...");

                    try
                    {
                        _cts.Cancel();
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
                        _server.CloseListenSocket();
                    }
                    break;
            }
        }

        private async Task<Result<RemoteServer>> SaveNewClientAsync(string clientIpAddress, int clientPort)
        {
            var parseIp = IpAddressHelper.ParseSingleIPv4Address(clientIpAddress);
            var clientIp = parseIp.Value;
            
            var connectionInfo = new ConnectionInfo {Port = clientPort};

            if (IpAddressHelper.IsLocalIpAddress(clientIpAddress))
            {
                connectionInfo.LocalIpAddress = clientIp;
            }
            else
            {
                connectionInfo.PublicIpAddress = clientIp;
            }

            var newClient = new RemoteServer
            {
                ConnectionInfo = connectionInfo
            };

            if (ClientAlreadyExists(newClient))
            {
                foreach (var server in _settings.RemoteServers)
                {
                    var localIp = server.ConnectionInfo.LocalIpAddress;
                    var publicIp = server.ConnectionInfo.PublicIpAddress;
                    var port = server.ConnectionInfo.Port;

                    if (Equals(localIp, clientIp) || Equals(publicIp, clientIp))
                    {
                        if (port == clientPort)
                        {
                            return Result.Ok(server);
                        }
                    }
                }
            }

                var clientAdded = await RequestAdditionalConnectionInfoFromClient(clientIp, newClient);
            if (clientAdded.Failure)
            {
                Console.WriteLine(clientAdded.Error);
            }

            return clientAdded;
        }
    }
}
