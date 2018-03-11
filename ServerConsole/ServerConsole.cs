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
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    using TplSocketServer;

    public class ServerConsole
    {
        const string SettingsFileName = "settings.xml";
        const string DefaultTransferFolderName = "transfer";

        const string PortChoicePrompt = "Enter the port number this server will use to handle connections";
        const string IpChoiceClient = "Which IP address would you like to use for this request?";
        const string NotifyLanTrafficOnly = "Unable to determine public IP address, this server will only be able to communicate with machines in the same local network.";
        const string ConnectionRefusedAdvice = "\nPlease verify that the port number on the client server is properly opened, this could entail modifying firewall or port forwarding settings depending on the operating system.";
        const string EmptyTransferFolderErrorMessage = "Currently there are no files available in transfer folder";
        const string FileAlreadyExists = "A file with the same name already exists in the download folder, please rename or remove this file in order to proceed.";
        const string DeadFileTransferError = "\nData is no longer being received from the client, file transfer has been cancelled";

        const int OneHalfSecondInMilliseconds = 500;
        const int OneSecondInMilliseconds = 1000;
        const int TwoSecondsInMillisconds = 2000;

        const int SendMessage = 1;
        const int SendFile = 2;
        const int GetFile = 3;
        const int ShutDown = 4;
        
        const int ReplyToTextMessage = 1;
        const int EndTextSession = 2;

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
        bool _progressBarInstantiated;
        bool _needToRewriteMenu;
        bool _errorOccurred;
        bool _clientResponseIsStalled;
        bool _fileTransferRejected;
        bool _noFilesAvailableForDownload;
        bool _fileTransferIsStalled;
        bool _abortFileTransfer;

        string _clientTransferFolderPath;
        IPAddress _clientPublicIp;
        List<(string filePath, long fileSize)> _fileInfoList;

        readonly CancellationTokenSource _cts;
        CancellationToken _token;
        ConsoleProgressBar _progress;
        StatusChecker _statusChecker;

        AppSettings _settings;
        ConnectionInfo _myInfo;
        TplSocketServer _server;

        string _textSessionIp;
        int _textSessionPort;

        public ServerConsole()
        {
            _cts = new CancellationTokenSource();
            _settingsFilePath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SettingsFileName}";
            _transferFolderPath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{DefaultTransferFolderName}";
        }

        public event EventHandler<ServerEventArgs> EventOccurred;

        public async Task<Result> RunServerAsync()
        {
            _token = _cts.Token;

            _settings = InitializeAppSettings();
            _myInfo = await GetLocalServerSettingsFromUser().ConfigureAwait(false);

            _server = new TplSocketServer(_settings, _myInfo.LocalIpAddress);
            _server.EventOccurred += HandleServerEvent;

            _waitingForServerToBeginAcceptingConnections = true;

            var result = Result.Fail(string.Empty);
            try
            {
                var listenTask =
                    Task.Run(() =>
                        _server.HandleIncomingConnectionsAsync(_myInfo.Port, _token),
                        _token);

                while (_waitingForServerToBeginAcceptingConnections) { }

                result = await ServerMenuAsync().ConfigureAwait(false);

                if (_progressBarInstantiated)
                {
                    _progress.Dispose();
                }

                await listenTask;
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

            return result;
        }

        AppSettings InitializeAppSettings()
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

        async Task<ConnectionInfo> GetLocalServerSettingsFromUser()
        {
            var portChoice =
                ConsoleStatic.GetPortNumberFromUser(PortChoicePrompt, true);

            var localIp = ConsoleStatic.GetLocalIpToBindTo();
            var publicIp = IPAddress.None;

            var retrievePublicIp = await Network.GetPublicIPv4AddressAsync().ConfigureAwait(false);
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

        async Task<Result> ServerMenuAsync()
        {
            while (true)
            {
                Console.WriteLine($"\nServer is listening for incoming requests on port {_myInfo.Port}\nLocal IP: {_myInfo.LocalIpString}\nPublic IP: {_myInfo.PublicIpString}");
                _errorOccurred = false;

                var menuResult = await GetMenuChoiceAsync().ConfigureAwait(false);
                if (menuResult.Failure)
                {
                    if (_errorOccurred)
                    {
                        ShutdownServer();
                        return Result.Fail(string.Empty);
                    }

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
                    ShutdownServer();
                    break;
                }

                var chooseClientResult = await ChooseClientAsync().ConfigureAwait(false);
                if (chooseClientResult.Failure)
                {
                    Console.WriteLine(chooseClientResult.Error);
                    Console.WriteLine("Returning to main menu...");
                    continue;
                }

                var clientInfo = chooseClientResult.Value;

                if (clientInfo.ConnectionInfo.IsEqualTo(_myInfo))
                {
                    return Result.Fail<RemoteServer>(
                        $"{clientInfo.ConnectionInfo.LocalIpAddress}:{clientInfo.ConnectionInfo.Port} is the same IP address and port number used by this server, returning to main menu.");
                }

                var ipChoice = ConsoleStatic.ChoosePublicOrLocalIpAddress(clientInfo, IpChoiceClient);
                var clientIpAddress = ConsoleStatic.GetChosenIpAddress(clientInfo.ConnectionInfo, ipChoice);
                var clientPort = clientInfo.ConnectionInfo.Port;
                
                var result = Result.Ok();
                switch (menuChoice)
                {
                    case SendMessage:
                        result = await SendTextMessageToClientAsync(clientIpAddress.ToString(), clientPort, _token).ConfigureAwait(false);
                        break;

                    case SendFile:
                        result = await SendFileToClientAsync(clientInfo, clientIpAddress.ToString(), clientPort, _token).ConfigureAwait(false);
                        break;

                    case GetFile:
                        result = await RequestFileListFromClientAsync(clientIpAddress.ToString(), clientPort, _token).ConfigureAwait(false);
                        break;
                }

                if (result.Success)
                {
                    Console.WriteLine("Returning to main menu...");
                    continue;
                }

                Console.WriteLine(result.Error);
                if (!ConsoleStatic.PromptUserToShutdownServer()) continue;

                ShutdownServer();
                break;
            }

            return Result.Ok();
        }

        async Task<Result<int>> GetMenuChoiceAsync()
        {
            await WriteMenuToScreenAsync(false).ConfigureAwait(false);
            _needToRewriteMenu = true;

            var input = Console.ReadLine();

            if (_errorOccurred)
            {
                return Result.Fail<int>(string.Empty);
            }

            if (_activeTextSession)
            {
                await PromptUserToReplyToTextMessageAsync().ConfigureAwait(false);
                return Result.Ok(0);
            }

            var validationResult = ConsoleStatic.ValidateNumberIsWithinRange(input, 1, 4);
            if (validationResult.Failure)
            {
                return validationResult;
            }

            Console.WriteLine(string.Empty);
            return Result.Ok(validationResult.Value);
        }

        async Task<Result> PromptUserToReplyToTextMessageAsync()
        {
            var userMenuChoice = 0;
            while (userMenuChoice is 0)
            {
                Console.WriteLine($"Reply to {_textSessionIp}:{_textSessionPort}?");
                Console.WriteLine("1. Yes");
                Console.WriteLine("2. No");
                var input = Console.ReadLine();
                Console.WriteLine(string.Empty);

                var validationResult = ConsoleStatic.ValidateNumberIsWithinRange(input, 1, 2);
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
                    await SendTextMessageToClientAsync(_textSessionIp, _textSessionPort, new CancellationToken()).ConfigureAwait(false);
                    break;

                case EndTextSession:
                    _activeTextSession = false;
                    break;
            }

            return Result.Ok();
        }

        async Task WriteMenuToScreenAsync(bool waitingForUserInput)
        {
            if (_activeTextSession)
            {
                await Task.Run(() => SendEnterToConsole.Execute(), _token).ConfigureAwait(false);
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

        void ShutdownServer()
        {
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
        }

        async Task<Result<RemoteServer>> ChooseClientAsync()
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

                var validationResult = ConsoleStatic.ValidateNumberIsWithinRange(input, 1, totalMenuChoices);
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
                return await AddNewClientAsync().ConfigureAwait(false);
            }

            return Result.Ok(_settings.RemoteServers[clientMenuChoice - 1]);
        }

        async Task<Result<RemoteServer>> AddNewClientAsync()
        {
            var newClient = new RemoteServer();
            var clientInfoIsValid = false;

            while (!clientInfoIsValid)
            {
                var addClientResult = ConsoleStatic.GetRemoteServerConnectionInfoFromUser();
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

            Result<RemoteServer> result;
            try
            {
                result = await RequestAdditionalConnectionInfoFromClientAsync(clientIp, newClient).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                return Result.Fail<RemoteServer>($"{ex.Message} ({ex.GetType()})");
            }

            return result;
        }

        async Task<Result<RemoteServer>> RequestAdditionalConnectionInfoFromClientAsync(IPAddress clientIp, RemoteServer client)
        {
            var clientPort = client.ConnectionInfo.Port;

            if (client.ConnectionInfo.IsEqualTo(_myInfo))
            {
                return Result.Fail<RemoteServer>($"{clientIp}:{clientPort} is the same IP address and port number used by this server, returning to main menu.");
            }

            if (ClientAlreadyAdded(client))
            {
                return Result.Fail<RemoteServer>(
                    "A client with the same IP address and Port # has already been added.");
            }

            Console.WriteLine("\nRequesting additional information from client...\n");

            _waitingForTransferFolderResponse = true;
            _clientResponseIsStalled = false;
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
                var userHint = string.Empty;
                if (sendFolderRequestResult.Error.Contains("Connection refused"))
                {
                    userHint = ConnectionRefusedAdvice;
                }

                return Result.Fail<RemoteServer>(
                    $"{sendFolderRequestResult.Error}{userHint}");
            }
            
            var oneSecondTimer = new Timer(HandleTimeout, true, TwoSecondsInMillisconds, Timeout.Infinite);

            while (_waitingForTransferFolderResponse)
            {
                if (_clientResponseIsStalled)
                {
                    oneSecondTimer.Dispose();
                    throw new TimeoutException();
                }
            }

            client.TransferFolder = _clientTransferFolderPath;

            if (Equals(client.ConnectionInfo.PublicIpAddress, IPAddress.None))
            {
                _waitingForPublicIpResponse = true;
                _clientResponseIsStalled = false;
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
                
                oneSecondTimer = new Timer(HandleTimeout, true, TwoSecondsInMillisconds, Timeout.Infinite);

                while (_waitingForPublicIpResponse)
                {
                    if (_clientResponseIsStalled)
                    {
                        oneSecondTimer.Dispose();
                        throw new TimeoutException();
                    }
                }
                client.ConnectionInfo.PublicIpAddress = _clientPublicIp;
            }

            Console.WriteLine("Thank you! Connection info for client has been successfully configured.\n");

            _settings.RemoteServers.Add(client);
            SaveSettingsToFile(_settings);
            return Result.Ok(client);
        }

        void HandleTimeout(object state)
        {
            _clientResponseIsStalled = true;
        }

        bool ClientAlreadyAdded(RemoteServer newClient)
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

        static void SaveSettingsToFile(AppSettings settings)
        {
            AppSettings.Serialize(settings, $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SettingsFileName}");
        }

        async Task<Result> SendTextMessageToClientAsync(string ipAddress, int port, CancellationToken token)
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
                    token).ConfigureAwait(false);

            return sendMessageResult.Failure ? sendMessageResult : Result.Ok();
        }

        async Task<Result> SendFileToClientAsync(RemoteServer clientInfo, string ipAddress, int port, CancellationToken token)
        {
            var selectFileResult = ConsoleStatic.ChooseFileToSend(_transferFolderPath);
            if (selectFileResult.Failure)
            {
                Console.WriteLine(selectFileResult.Error);
                return Result.Ok();
            }

            var fileToSend = selectFileResult.Value;
            _waitingForConfirmationMessage = true;
            _abortFileTransfer = false;
            _fileTransferRejected = false;

            var sendFileTask =
                Task.Run(() =>
                    _server.SendFileAsync(
                        ipAddress,
                        port,
                        fileToSend,
                        clientInfo.TransferFolder,
                        token),
                    token);

            while (_waitingForConfirmationMessage)
            {
                if (_abortFileTransfer)
                {
                    return Result.Fail("Aborting file transfer, client says that data is no longer being received");
                }
            }

            var sendFileResult = await sendFileTask;
            if (_fileTransferRejected)
            {
                return Result.Fail(
                    "Client rejected the transfer, file with same name and size has already been downloaded,");
            }

            return sendFileResult.Failure ? sendFileResult : Result.Ok();
        }

        async Task<Result> RequestFileListFromClientAsync(string ipAddress, int port, CancellationToken token)
        {
            _waitingForFileListResponse = true;
            _waitingForDownloadToComplete = true;
            _noFilesAvailableForDownload = false;
            _fileTransferIsStalled = false;

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

            if (_noFilesAvailableForDownload)
            {
                return Result.Fail("Client has no files available to download.");
            }

            var fileDownloadResult = await DownloadFileFromClient(ipAddress, port).ConfigureAwait(false);
            if (fileDownloadResult.Failure)
            {
                return fileDownloadResult;
            }

            while (_waitingForDownloadToComplete) { }

            if (!_fileTransferIsStalled)
            {
                return Result.Ok();
            }

            var notifyCientResult =
                await _server.NotifyClientDataIsNotBeingReceived(
                        ipAddress,
                        port,
                        _myInfo.LocalIpAddress.ToString(),
                        _myInfo.Port,
                        token)
                    .ConfigureAwait(false);

            return notifyCientResult.Failure
                ? Result.Fail($"Error occurred when notifying client that file transfer data is no longer being received:\n{notifyCientResult.Error}")
                : Result.Fail("File transfer cancelled, data is no longer being received from client");            
        }

        async Task<Result> DownloadFileFromClient(string remoteIp, int remotePort)
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

            return getFileResult.Success
                ? Result.Ok()
                : getFileResult;
        }

        Result<string> ChooseFileToGet()
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
                    var fileSize = _fileInfoList[i].fileSize;
                    Console.WriteLine($"{i + 1}. {fileName} ({FileHelper.FileSizeToString(fileSize)})");
                }

                Console.WriteLine($"{returnToMainMenu}. Return to Main Menu");

                var input = Console.ReadLine();

                var validationResult = ConsoleStatic.ValidateNumberIsWithinRange(input, 1, totalMenuChoices);
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

        async void HandleServerEvent(object sender, ServerEventArgs serverEvent)
        {
            EventOccurred?.Invoke(this, serverEvent);

            switch (serverEvent.EventType)
            {
                case ServerEventType.AcceptConnectionAttemptStarted:
                    _waitingForServerToBeginAcceptingConnections = false;
                    break;

                case ServerEventType.ReceiveTextMessageCompleted:

                    await SaveNewClientAsync(serverEvent.RemoteServerIpAddress, serverEvent.RemoteServerPortNumber).ConfigureAwait(false);
                    _textSessionIp = serverEvent.RemoteServerIpAddress;
                    _textSessionPort = serverEvent.RemoteServerPortNumber;
                    //_activeTextSession = true;

                    if (serverEvent.TextMessage.Contains(EmptyTransferFolderErrorMessage))
                    {
                        _waitingForFileListResponse = false;
                        _noFilesAvailableForDownload = true;
                    }
                    else if (serverEvent.TextMessage.Contains(FileAlreadyExists))
                    {
                        _waitingForConfirmationMessage = false;
                        _fileTransferRejected = true;
                    }
                    else
                    {
                        Console.WriteLine($"\n{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} says:");
                        Console.WriteLine(serverEvent.TextMessage);

                        await WriteMenuToScreenAsync(true).ConfigureAwait(false);
                    }

                    break;

                case ServerEventType.ReceiveFileBytesStarted:

                    _statusChecker = new StatusChecker(OneSecondInMilliseconds, 3);
                    _statusChecker.FileTransferIsDead += HandleDeadFileTransfer;

                    _progress = new ConsoleProgressBar
                    {
                        FileSizeInBytes = serverEvent.FileSizeInBytes,
                        NumberOfBlocks = 20,
                        StartBracket = "|",
                        EndBracket = "|",
                        CompletedBlock = "|",
                        UncompletedBlock = "-",
                        AnimationSequence = ConsoleProgressBar.ExplodingAnimation
                    };

                    _progressBarInstantiated = true;
                    Console.WriteLine(Environment.NewLine);
                    break;

                case ServerEventType.FileTransferProgress:

                    _statusChecker.CheckInTransferProgress();
                    _progress.BytesReceived = serverEvent.TotalFileBytesReceived;
                    _progress.Report(serverEvent.PercentComplete);
                    break;

                case ServerEventType.AbortOutboundFileTransfer:
                    _abortFileTransfer = true;
                    _waitingForConfirmationMessage = false;
                    break;

                case ServerEventType.ReceiveFileBytesCompleted:

                    _statusChecker.FileTransferComplete();
                    _progress.BytesReceived = serverEvent.FileSizeInBytes;
                    _progress.Report(1);
                    await Task.Delay(OneHalfSecondInMilliseconds).ConfigureAwait(false);
                    _progress.Dispose();

                    Console.WriteLine($"\n\nTransfer Start Time:\t\t{serverEvent.FileTransferStartTime.ToLongTimeString()}");
                    Console.WriteLine($"Transfer Complete Time:\t\t{serverEvent.FileTransferCompleteTime.ToLongTimeString()}");
                    Console.WriteLine($"Elapsed Time:\t\t\t{serverEvent.FileTransferElapsedTimeString}");
                    Console.WriteLine($"Transfer Rate:\t\t\t{serverEvent.FileTransferRate}\n");

                    if (!_requestedFileFromClient)
                    {
                        await SaveNewClientAsync(
                            serverEvent.RemoteServerIpAddress,
                            serverEvent.RemoteServerPortNumber).ConfigureAwait(false);
                    }

                    _waitingForDownloadToComplete = false;
                    _requestedFileFromClient = false;

                    await WriteMenuToScreenAsync(_needToRewriteMenu).ConfigureAwait(false);
                    break;

                case ServerEventType.ReceiveConfirmationMessageCompleted:
                    _waitingForConfirmationMessage = false;
                    await WriteMenuToScreenAsync(true).ConfigureAwait(false);
                    break;

                case ServerEventType.ReceiveFileListRequestCompleted:
                    await SaveNewClientAsync(serverEvent.RemoteServerIpAddress, serverEvent.RemoteServerPortNumber).ConfigureAwait(false);
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
                    await WriteMenuToScreenAsync(true).ConfigureAwait(false);
                    break;

                case ServerEventType.ReceivePublicIpResponseCompleted:

                    var clientPublicIpString = serverEvent.PublicIpAddress;
                    var parseIp = Network.ParseSingleIPv4Address(clientPublicIpString);
                    _clientPublicIp = parseIp.Value;

                    _waitingForPublicIpResponse = false;
                    break;

                case ServerEventType.ErrorOccurred:
                    _errorOccurred = true;
                    break;
            }
        }

        async void HandleDeadFileTransfer()
        {
            _fileTransferIsStalled = true;
            _progress.Dispose();

            Console.WriteLine(DeadFileTransferError);

            _waitingForDownloadToComplete = false;
            _requestedFileFromClient = false;

            await WriteMenuToScreenAsync(_needToRewriteMenu).ConfigureAwait(false);
        }

        async Task<Result<RemoteServer>> SaveNewClientAsync(string clientIpAddress, int clientPort)
        {
            var parseIp = Network.ParseSingleIPv4Address(clientIpAddress);
            var clientIp = parseIp.Value;

            var connectionInfo = new ConnectionInfo { Port = clientPort };

            if (Network.IpAddressIsInPrivateAddressSpace(clientIpAddress))
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

            if (ClientAlreadyAdded(newClient))
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

            var clientAdded = await RequestAdditionalConnectionInfoFromClientAsync(clientIp, newClient).ConfigureAwait(false);
            if (clientAdded.Failure)
            {
                Console.WriteLine(clientAdded.Error);
            }

            return clientAdded;
        }
    }
}
