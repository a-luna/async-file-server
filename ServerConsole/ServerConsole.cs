using System.Net.Sockets;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.Logging;

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
        const string NotifyLanTrafficOnly = "Unable to determine public IP address, this server will only be able to communicate with machines in the same local network.";
        const string ConnectionRefusedAdvice = "\nPlease verify that the port number on the client server is properly opened, this could entail modifying firewall or port forwarding settings depending on the operating system.";
        const string FileAlreadyExists = "\nThe client rejected the file transfer since a file by the same name already exists";
        const string FileTransferCancelled = "\nCancelled file transfer, client stopped receiving data and file transfer is incomplete.";

        const int OneHalfSecondInMilliseconds = 500;
        const int TwoSecondsInMillisconds = 2000;
        const int FiveSecondsInMilliseconds = 5000;

        const int SendMessage = 1;
        const int SendFile = 2;
        const int GetFile = 3;
        const int ShutDown = 4;
        
        const int ReplyToTextMessage = 1;
        const int EndTextSession = 2;

        readonly string _settingsFilePath;
        readonly string _defaultTransferFolderPath;

        string _clientIpAddress;
        int _clientPort;
        string _clientTransferFolder;

        bool _waitingForServerToBeginAcceptingConnections = true;
        bool _waitingForTransferFolderResponse = true;
        bool _waitingForPublicIpResponse = true;
        bool _waitingForFileListResponse = true;
        bool _waitingForDownloadToComplete = true;
        bool _waitingForConfirmationMessage = true;
        bool _activeTextSession;
        bool _waitingForUserInput;
        bool _progressBarInstantiated;
        bool _errorOccurred;
        bool _clientResponseIsStalled;
        bool _fileTransferRejected;
        bool _noFilesAvailableForDownload;
        bool _fileTransferStalled;
        bool _fileTransferCanceled;

         string _downloadFileName;
        int _retryCounter;
        ProgressEventArgs _fileStalledInfo;

        string _clientTransferFolderPath;
        IPAddress _clientPublicIp;
        List<(string filePath, long fileSize)> _fileInfoList;

        readonly CancellationTokenSource _cts;
        CancellationToken _token;
        ConsoleProgressBar _progress;
        //StatusChecker _statusChecker;

        AppSettings _settings;
        ConnectionInfo _myInfo;
        TplSocketServer _server;

        string _textSessionClientIpAddress;
        int _textSessionClientPort;

        readonly Logger _log = new Logger(typeof(TplSocketServer));
        AutoResetEvent _signalExitRetryDownloadLogic = new AutoResetEvent(false);

        public ServerConsole()
        {
            _cts = new CancellationTokenSource();
            _settingsFilePath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SettingsFileName}";
            _defaultTransferFolderPath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{DefaultTransferFolderName}";
        }

        public event EventHandler<ServerEvent> EventOccurred;

        public async Task<Result> RunServerAsync()
        {
            _token = _cts.Token;
            _settings = InitializeAppSettings();
            _myInfo = await GetLocalServerSettingsFromUser().ConfigureAwait(false);

            _server = new TplSocketServer(_myInfo.LocalIpAddress, _myInfo.Port)
            {
                SocketSettings = _settings.SocketSettings,
                TransferFolderPath = _settings.TransferFolderPath
            };

            _server.EventOccurred += HandleServerEvent;
            _server.LoggingEnabled = true;

            _waitingForServerToBeginAcceptingConnections = true;

            var result = Result.Fail(string.Empty);
            try
            {
                var listenTask =
                    Task.Run(() =>
                        _server.HandleIncomingConnectionsAsync(_token),
                        _token);

                while (_waitingForServerToBeginAcceptingConnections) { }

                result = await ServerMenuAsync().ConfigureAwait(false);

                if (_progressBarInstantiated)
                {
                    _progress.Dispose();
                    _progressBarInstantiated = false;
                }

                _waitingForUserInput = false;
                await _server.ShutdownServerAsync();
                var shutdownServerResult = await listenTask;
                if (shutdownServerResult.Failure)
                {
                    Console.WriteLine(shutdownServerResult.Error);
                }

                _server.EventOccurred -= HandleServerEvent;
            }
            catch (AggregateException ex)
            {
                _log.Error("Error raised in method RunServerAsync", ex);
                Console.WriteLine("\nException messages:");
                foreach (var ie in ex.InnerExceptions)
                {
                    _log.Error("Error raised in method RunServerAsync", ie);
                    Console.WriteLine($"\t{ie.GetType().Name}: {ie.Message}");
                }
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method RunServerAsync", ex);
                Console.WriteLine($"{ex.Message} ({ex.GetType()}) raised in method ServerConsole.RunServerAsync");                
            }
            catch (Exception ex)
            {
                _log.Error("Error raised in method RunServerAsync", ex);
                Console.WriteLine($"{ex.Message} ({ex.GetType()}) raised in method ServerConsole.RunServerAsync");
            }

            return result;
        }
        
        public void CloseListenSocket()
        {
            _server.CloseListenSocket();
        }

        AppSettings InitializeAppSettings()
        {
            var settings = new AppSettings
            {
                MaxDownloadAttempts = 3,
                TransferFolderPath = _defaultTransferFolderPath,
                TransferUpdateInterval = 0.0025f
            };

            var filePath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SettingsFileName}";
            AppSettings.SaveToFile(settings, filePath);

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
                _errorOccurred = false;
                var menuResult = await GetMenuChoiceAsync().ConfigureAwait(false);

                if (menuResult.Failure)
                {
                    if (_errorOccurred)
                    {
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
                    Console.WriteLine("\nServer is shutting down");
                    return Result.Ok();
                }

                var chooseClientResult = await ChooseClientAsync().ConfigureAwait(false);
                if (chooseClientResult.Failure)
                {
                    Console.WriteLine(chooseClientResult.Error);
                    Console.WriteLine("Returning to main menu...");
                    continue;
                }

                var clientInfo = chooseClientResult.Value;

                var result =
                    await ExecuteMenuChoiceAsync(clientInfo, menuChoice).ConfigureAwait(false);

                if (result.Failure)
                {
                    Console.WriteLine(result.Error);
                    if (ConsoleStatic.PromptUserYesOrNo("Shutdown server?"))
                    {
                        return Result.Ok();
                    }
                    continue;
                }
                
                Console.WriteLine("\nReturning to main menu...");
            }
        }

        async Task<Result<int>> GetMenuChoiceAsync()
        {
            await WriteMenuToScreenAsync().ConfigureAwait(false);

            _waitingForUserInput = true;
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
            
            return Result.Ok(validationResult.Value);
        }

        async Task<Result> PromptUserToReplyToTextMessageAsync()
        {
            var userPrompt = $"Reply to {_textSessionClientIpAddress}:{_textSessionClientPort}?";
            if (ConsoleStatic.PromptUserYesOrNo(userPrompt))
            {
                await SendTextMessageToClientAsync(
                    _textSessionClientIpAddress,
                    _textSessionClientPort,
                    new CancellationToken()).ConfigureAwait(false);
            }
            else
            {
                _activeTextSession = false;
            }

            return Result.Ok();
        }

        async Task WriteMenuToScreenAsync()
        {
            if (_activeTextSession)
            {
                await Task.Run(() => SendEnterToConsole.Execute(), _token).ConfigureAwait(false);
                return;
            }

            Console.WriteLine($"\nServer is listening for incoming requests on port {_myInfo.Port}\nLocal IP:\t{_myInfo.LocalIpString}\nPublic IP:\t{_myInfo.PublicIpString}");
            Console.WriteLine("\nPlease make a choice from the menu below:");
            Console.WriteLine("1. Send Text Message");
            Console.WriteLine("2. Send File");
            Console.WriteLine("3. Get File");
            Console.WriteLine("4. Shutdown");
        }

        async Task<Result<RemoteServer>> ChooseClientAsync()
        {
            var clientMenuChoice = 0;
            var totalMenuChoices = _settings.RemoteServers.Count + 2;
            var addNewClient = _settings.RemoteServers.Count + 1;
            var returnToMainMenu = totalMenuChoices;

            while (clientMenuChoice == 0)
            {
                Console.WriteLine("\nChoose a remote server for this request:");

                foreach (var i in Enumerable.Range(0, _settings.RemoteServers.Count))
                {
                    var thisClient = _settings.RemoteServers[i];
                    Console.WriteLine($"\n{i + 1}.\tLocal IP: {thisClient.ConnectionInfo.LocalIpString}\n\tPublic IP: {thisClient.ConnectionInfo.PublicIpString}\n\tPort: {thisClient.ConnectionInfo.Port}");
                }

                Console.WriteLine($"\n{addNewClient}. Add New Client");
                Console.WriteLine($"{returnToMainMenu}. Return to Main Menu");

                var input = Console.ReadLine();

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

            var clientInfo = _settings.RemoteServers[clientMenuChoice - 1];
            if (clientInfo.ConnectionInfo.IsEqualTo(_myInfo))
            {
                return Result.Fail<RemoteServer>(
                    $"{clientInfo.ConnectionInfo.LocalIpAddress}:{clientInfo.ConnectionInfo.Port} is the same IP address and port number used by this server.");
            }

            ConsoleStatic.SetSessionIpAddress(clientInfo);

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
                _log.Error("Error raised in method AddNewClientAsync", ex);
                return Result.Fail<RemoteServer>($"{ex.Message} ({ex.GetType()})");
            }

            return result;
        }

        async Task<Result<RemoteServer>> RequestAdditionalConnectionInfoFromClientAsync(IPAddress clientIp, RemoteServer client)
        {
            var clientPort = client.ConnectionInfo.Port;

            if (client.ConnectionInfo.IsEqualTo(_myInfo))
            {
                return Result.Fail<RemoteServer>($"{clientIp}:{clientPort} is the same IP address and port number used by this server.");
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
                await _server.RequestTransferFolderPathAsync(
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
                    await _server.RequestPublicIpAsync(
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
            var filePath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SettingsFileName}";
            AppSettings.SaveToFile(_settings, filePath);
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

        async Task<Result> ExecuteMenuChoiceAsync(RemoteServer clientInfo, int menuChoice)
        {
            _clientIpAddress = clientInfo.ConnectionInfo.SessionIpAddress.ToString();
            _clientPort = clientInfo.ConnectionInfo.Port;
            _clientTransferFolder = clientInfo.TransferFolder;
            
            switch (menuChoice)
            {
                case SendMessage:

                    return await SendTextMessageToClientAsync(
                        _clientIpAddress,
                        _clientPort,
                        _token).ConfigureAwait(false);

                case SendFile:

                    return await SendFileToClientAsync(
                        _clientIpAddress,
                        _clientPort,
                        _clientTransferFolder,
                        _token).ConfigureAwait(false);

                case GetFile:

                    return await RequestFileListFromClientAsync(
                        _clientIpAddress,
                        _clientPort,
                        _clientTransferFolder,
                        _token).ConfigureAwait(false);
            }

            return Result.Ok();
        }

        async Task<Result> SendTextMessageToClientAsync(string ipAddress, int port, CancellationToken token)
        {
            Console.WriteLine($"Please enter a text message to send to {ipAddress}:{port}");
            var message = Console.ReadLine();

            _waitingForUserInput = false;

            var sendMessageResult =
                await _server.SendTextMessageAsync(
                    message,
                    ipAddress,
                    port,
                    _myInfo.LocalIpAddress.ToString(),
                    _myInfo.Port,
                    token).ConfigureAwait(false);

            return sendMessageResult.Success
                ? Result.Ok()
                : sendMessageResult;
        }

        async Task<Result> SendFileToClientAsync(string ipAddress, int port, string transferFolderPath, CancellationToken token)
        {
            var selectFileResult = ConsoleStatic.ChooseFileToSend(_settings.TransferFolderPath);
            if (selectFileResult.Failure)
            {
                Console.WriteLine(selectFileResult.Error);
                return Result.Ok();
            }

            var fileToSend = selectFileResult.Value;

            _waitingForUserInput = false;            
            _waitingForConfirmationMessage = true;
            _fileTransferRejected = false;
            _fileTransferCanceled = false;

            var sendFileResult =
                await _server.SendFileAsync(
                    ipAddress,
                    port,
                    fileToSend,
                    transferFolderPath,
                    token);

            if (sendFileResult.Failure)
            {
                return sendFileResult;
            }

            while (_waitingForConfirmationMessage) {}
            if (_fileTransferRejected)
            {
                return Result.Fail(FileAlreadyExists);
            }

            if (_fileTransferCanceled)
            {
                return Result.Fail(FileTransferCancelled);
            }
            
            return Result.Ok();
        }

        async Task<Result> RequestFileListFromClientAsync(string ipAddress, int port, string remoteFolder, CancellationToken token)
        {
            _waitingForFileListResponse = true;
            _noFilesAvailableForDownload = false;

            var requestFileListResult =
                await _server.RequestFileListAsync(
                        ipAddress,
                        port,
                        _myInfo.LocalIpAddress.ToString(),
                        _myInfo.Port,
                        remoteFolder,
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

            _waitingForDownloadToComplete = true;
            _fileTransferStalled = false;

            var fileDownloadResult = await DownloadFileFromClient(ipAddress, port).ConfigureAwait(false);
            if (fileDownloadResult.Failure)
            {
                return fileDownloadResult;
            }

            while (_waitingForDownloadToComplete) { }

            return _fileTransferStalled
                ? Result.Fail("Notified client that file transfer has stalled")
                : Result.Ok();
        }
        
        async Task<Result> DownloadFileFromClient(string remoteIp, int remotePort)
        {
            var selectFileResult = ChooseFileToGet();
            if (selectFileResult.Failure)
            {
                // In this case, "Failure" only occcurs because the user chose to 
                // return to the main menu.
                _waitingForDownloadToComplete = false;
                return Result.Ok();
            }
            
            var fileToGet = selectFileResult.Value;
            _waitingForUserInput = false;

            var getFileResult =
                await _server.GetFileAsync(
                    remoteIp,
                    remotePort,
                    fileToGet,
                    _myInfo.LocalIpAddress.ToString(),
                    _myInfo.Port,
                    _settings.TransferFolderPath,
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

        async void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            _log.Info(serverEvent.ToString());
            EventOccurred?.Invoke(this, serverEvent);

            switch (serverEvent.EventType)
            {
                case EventType.AcceptConnectionAttemptStarted:
                    _waitingForServerToBeginAcceptingConnections = false;
                    return;

                case EventType.ReadTextMessageComplete:
                    await HandleReadTextMessageComplete(serverEvent);
                    break;
                    
                case EventType.ReadInboundFileTransferInfoComplete:
                    _clientIpAddress = serverEvent.RemoteServerIpAddress;
                    _clientPort = serverEvent.RemoteServerPortNumber;
                    _downloadFileName = serverEvent.FileName;
                    _retryCounter = 0;
                    return;

                case EventType.ReceiveFileBytesStarted:
                    HandleReceiveFileBytesStarted(serverEvent);
                    return;

                case EventType.UpdateFileTransferProgress:                    
                    _progress.BytesReceived = serverEvent.TotalFileBytesReceived;
                    _progress.Report(serverEvent.PercentComplete);
                    return;

                case EventType.ReceiveFileBytesComplete:
                    await HandleReceiveFileBytesComplete(serverEvent);
                    break;
                    
                case EventType.ReceiveConfirmationMessageComplete:
                    _waitingForConfirmationMessage = false;
                    break;

                case EventType.ReadFileListRequestComplete:
                    await SaveNewClientAsync(serverEvent.RemoteServerIpAddress, serverEvent.RemoteServerPortNumber).ConfigureAwait(false);
                    return;

                case EventType.ReadFileListResponseComplete:
                    _waitingForFileListResponse = false;
                    _fileInfoList = serverEvent.FileInfoList;
                    return;

                case EventType.ReadTransferFolderResponseComplete:
                    _clientTransferFolderPath = serverEvent.RemoteFolder;
                    _waitingForTransferFolderResponse = false;
                    return;
                    
                case EventType.ReadPublicIpResponseComplete:
                    HandleReadPublicIpResponseComplete(serverEvent);
                    return;

                case EventType.SendFileTransferStalledComplete:
                    HandleFileTransferStalled();
                    break;

                case EventType.SendFileTransferRejectedComplete:
                    _waitingForUserInput = true;
                    _signalExitRetryDownloadLogic.WaitOne();
                    break;

                case EventType.ReceiveFileTransferRejectedComplete:
                    _waitingForConfirmationMessage = false;
                    _fileTransferRejected = true;
                    break;

                case EventType.ReceiveFileTransferStalledComplete:
                    _waitingForConfirmationMessage = false;
                    _fileTransferCanceled = true;
                    break;

                case EventType.ReceiveNotificationNoFilesToDownloadComplete:
                    _waitingForFileListResponse = false;
                    _noFilesAvailableForDownload = true;
                    break;

                case EventType.ErrorOccurred:
                    _errorOccurred = true;
                    break;

                default:
                    return;
            }

            if (_waitingForUserInput)
            {
                await WriteMenuToScreenAsync().ConfigureAwait(false);
            }
        }

        void HandleReadPublicIpResponseComplete(ServerEvent serverEvent)
        {
            var clientPublicIpString = serverEvent.PublicIpAddress;
            var parseIp = Network.ParseSingleIPv4Address(clientPublicIpString);
            _clientPublicIp = parseIp.Value;

            _waitingForPublicIpResponse = false;
        }

        async Task HandleReadTextMessageComplete(ServerEvent serverEvent)
        {
            await SaveNewClientAsync(serverEvent.RemoteServerIpAddress, serverEvent.RemoteServerPortNumber)
                .ConfigureAwait(false);
            _textSessionClientIpAddress = serverEvent.RemoteServerIpAddress;
            _textSessionClientPort = serverEvent.RemoteServerPortNumber;
            //_activeTextSession = true;                  

            Console.WriteLine($"\n{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} says:");
            Console.WriteLine(serverEvent.TextMessage);

            _waitingForUserInput = true;
        }

        void HandleReceiveFileBytesStarted(ServerEvent serverEvent)
        {
            //_statusChecker = new StatusChecker(1000);
            //_statusChecker.NoActivityEvent += HandleStalledFileTransfer;

            _progress = new ConsoleProgressBar
            {
                FileSizeInBytes = serverEvent.FileSizeInBytes,
                NumberOfBlocks = 20,
                StartBracket = "|",
                EndBracket = "|",
                CompletedBlock = "|",
                UncompletedBlock = "-",
                DisplayAnimation = false,
                DisplayLastRxTime = true,
            };

            _progress.FileTransferStalled += HandleStalledFileTransfer;
            _progressBarInstantiated = true;
            Console.WriteLine(Environment.NewLine);
        }

        async Task HandleReceiveFileBytesComplete(ServerEvent serverEvent)
        {
            _progress.BytesReceived = serverEvent.FileSizeInBytes;
            _progress.Report(1);
            await Task.Delay(OneHalfSecondInMilliseconds).ConfigureAwait(false);

            _progress.Dispose();
            _progressBarInstantiated = false;

            Console.WriteLine($"\n\nTransfer Start Time:\t\t{serverEvent.FileTransferStartTime.ToLongTimeString()}");
            Console.WriteLine($"Transfer Complete Time:\t\t{serverEvent.FileTransferCompleteTime.ToLongTimeString()}");
            Console.WriteLine($"Elapsed Time:\t\t\t{serverEvent.FileTransferElapsedTimeString}");
            Console.WriteLine($"Transfer Rate:\t\t\t{serverEvent.FileTransferRate}");

            if (_waitingForUserInput)
            {
                await SaveNewClientAsync(
                    serverEvent.RemoteServerIpAddress,
                    serverEvent.RemoteServerPortNumber).ConfigureAwait(false);
            }

            _waitingForDownloadToComplete = false;
            _retryCounter = 0;
            _signalExitRetryDownloadLogic.Set();
        }
        
        async void HandleStalledFileTransfer(object sender, ProgressEventArgs eventArgs)
        {
            _fileStalledInfo = eventArgs;
            _fileTransferStalled = true;
            _waitingForDownloadToComplete = false;

            _progress.Dispose();
            _progressBarInstantiated = false;

            var notifyStalledResult = 
            await NotifyClientThatFileTransferHasStalledAsync(
                _clientIpAddress,
                _clientPort,
                new CancellationToken());

            if (notifyStalledResult.Failure)
            {
                Console.WriteLine(notifyStalledResult.Error);
            }

            if (_retryCounter >= _settings.MaxDownloadAttempts)
            {
                var maxRetriesReached =
                    "Maximum # of attempts to complete stalled file transfer reached or exceeded " +
                    $"({_settings.MaxDownloadAttempts} failed attempts for \"{_downloadFileName}\")";

                Console.WriteLine(maxRetriesReached);

                var folder = _settings.TransferFolderPath;
                var filePath1 = $"{folder}{Path.DirectorySeparatorChar}{_downloadFileName}";
                var filePath2 = Path.Combine(folder, _downloadFileName);

                FileHelper.DeleteFileIfAlreadyExists(filePath1);

                _signalExitRetryDownloadLogic.Set();
                return;
            }

            var userPrompt = $"Try again to download file \"{_downloadFileName}\" from {_clientIpAddress}:{_clientPort}?";
            if (ConsoleStatic.PromptUserYesOrNo(userPrompt))
            {
                _retryCounter++;
                await _server.RetryCanceledFileTransfer(
                    _clientIpAddress,
                    _clientPort,
                    new CancellationToken());
            }
        }

        async Task<Result> NotifyClientThatFileTransferHasStalledAsync(
            string ipAddress,
            int port,
            CancellationToken token)
        {
            var notifyCientResult =
                await _server.SendNotificationFileTransferStalledAsync(
                        ipAddress,
                        port,
                        _myInfo.LocalIpAddress.ToString(),
                        _myInfo.Port,
                        token).ConfigureAwait(false);

            return notifyCientResult.Failure
                ? Result.Fail($"\nError occurred when notifying client that file transfer data is no longer being received:\n{notifyCientResult.Error}")
                : Result.Fail("File transfer canceled, data is no longer being received from client");
        }

        private void HandleFileTransferStalled()
        {
            var sinceLastActivity = DateTime.Now - _fileStalledInfo.LastDataReceived;
            Console.WriteLine($"\n\nFile transfer has stalled, {sinceLastActivity.ToFormattedString()} elapsed since last data received");
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
