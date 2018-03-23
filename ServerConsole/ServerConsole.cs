using ServerConsole.Commands.Menus;

namespace ServerConsole
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console;
    using AaronLuna.Common.Extensions;
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    using Commands.CompositeCommands;
    using Commands.Getters;

    using TplSocketServer;

    public class ServerConsole
    {
        const string SettingsFileName = "settings.xml";
        const string FileAlreadyExists = "\nThe client rejected the file transfer since a file by the same name already exists";
        const string FileTransferCancelled = "\nCancelled file transfer, client stopped receiving data and file transfer is incomplete.";

        const int OneHalfSecondInMilliseconds = 500;

        const int SendMessage = 1;
        const int SendFile = 2;
        const int GetFile = 3;
        const int ShutDown = 4;
        
        const int ReplyToTextMessage = 1;
        const int EndTextSession = 2;
        
        readonly Logger _log = new Logger(typeof(TplSocketServer));

        string _settingsFilePath;
        ConsoleProgressBar _progress;
        bool _progressBarInstantiated;
        AutoResetEvent _signalExitRetryDownloadLogic = new AutoResetEvent(false);
        CancellationToken _token = new CancellationToken();
        AppState _state;

        public ServerConsole()
        {
            _settingsFilePath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SettingsFileName}";
            _state = new AppState();            
        }

        public event EventHandler<ServerEvent> EventOccurred;

        public async Task<Result> RunServerAsync()
        {
            var initializeServerCommand = new InitializeServerCommand(_state, _settingsFilePath);
            var initializeServerResult = await initializeServerCommand.ExecuteAsync();

            if (initializeServerResult.Failure)
            {
                return Result.Fail(initializeServerResult.Error);
            }

            _state.Server.EventOccurred += HandleServerEvent;
            _state.Server.FileTransferProgress += HandleFileTransferProgress;

            var result = Result.Fail(string.Empty);
            
            try
            {
                var listenTask =
                    Task.Run(() =>
                        _state.Server.HandleIncomingConnectionsAsync(_token),
                        _token);

                while (_state.WaitingForServerToBeginAcceptingConnections) { }
                _state.ServerIsListening = true;

                var mainMenu = new MainMenu(_state);
                result = await mainMenu.ExecuteAsync();

                if (_progressBarInstantiated)
                {
                    _progress.Dispose();
                    _progressBarInstantiated = false;
                }

                _state.WaitingForUserInput = false;
                await _state.Server.ShutdownServerAsync();
                var shutdownServerResult = await listenTask;
                if (shutdownServerResult.Failure)
                {
                    Console.WriteLine(shutdownServerResult.Error);
                }

                _state.Server.EventOccurred -= HandleServerEvent;
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
            _state.ServerIsListening = false;
            _state.Server.CloseListenSocket();
        }

        async Task<Result<int>> GetMenuChoiceAsync()
        {
            //await WriteMenuToScreenAsync().ConfigureAwait(false);

            _state.WaitingForUserInput = true;
            var input = Console.ReadLine();

            if (_state.ErrorOccurred)
            {
                return Result.Fail<int>(string.Empty);
            }

            if (_state.ActiveTextSession)
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
            var textIp = _state.ClientSessionIpAddress;
            var textPort = _state.ClientServerPort;
            var userPrompt = $"Reply to {textIp}:{textPort}?";
            if (ConsoleStatic.PromptUserYesOrNo(userPrompt))
            {
                //await SendTextMessageToClientAsync(
                //    textIp.ToString(),
                //    textPort,
                //    new CancellationToken()).ConfigureAwait(false);
            }
            else
            {
                _state.ActiveTextSession = false;
            }

            return Result.Ok();
        }

        async Task<Result> SendFileToClientAsync(string ipAddress, int port, string transferFolderPath, CancellationToken token)
        {
            var selectFileResult = OldStatic.ChooseFileToSend(_state.Settings.TransferFolderPath);
            if (selectFileResult.Failure)
            {
                Console.WriteLine(selectFileResult.Error);
                return Result.Ok();
            }

            var fileToSend = selectFileResult.Value;

            _state.WaitingForUserInput = false;
            _state.WaitingForConfirmationMessage = true;
            _state.FileTransferRejected = false;
            _state.FileTransferCanceled = false;

            var sendFileResult =
                await _state.Server.SendFileAsync(
                    ipAddress,
                    port,
                    fileToSend,
                    transferFolderPath,
                    token);

            if (sendFileResult.Failure)
            {
                return sendFileResult;
            }

            while (_state.WaitingForConfirmationMessage) {}
            if (_state.FileTransferRejected)
            {
                return Result.Fail(FileAlreadyExists);
            }

            if (_state.FileTransferCanceled)
            {
                return Result.Fail(FileTransferCancelled);
            }
            
            return Result.Ok();
        }

        async Task<Result> RequestFileListFromClientAsync(string ipAddress, int port, string remoteFolder, CancellationToken token)
        {
            _state.WaitingForFileListResponse = true;
            _state.NoFilesAvailableForDownload = false;

            var requestFileListResult =
                await _state.Server.RequestFileListAsync(
                        ipAddress,
                        port,
                        _state.MyInfo.LocalIpAddress.ToString(),
                        _state.MyInfo.Port,
                        remoteFolder,
                        token)
                    .ConfigureAwait(false);

            if (requestFileListResult.Failure)
            {
                return Result.Fail(
                    $"Error requesting list of available files from client:\n{requestFileListResult.Error}");
            }

            while (_state.WaitingForFileListResponse) { }

            if (_state.NoFilesAvailableForDownload)
            {
                return Result.Fail("Client has no files available to download.");
            }

            _state.WaitingForDownloadToComplete = true;
            _state.FileTransferStalled = false;

            var fileDownloadResult = await DownloadFileFromClient(ipAddress, port).ConfigureAwait(false);
            if (fileDownloadResult.Failure)
            {
                return fileDownloadResult;
            }

            while (_state.WaitingForDownloadToComplete) { }

            return _state.FileTransferStalled
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
                _state.WaitingForDownloadToComplete = false;
                return Result.Ok();
            }
            
            var fileToGet = selectFileResult.Value;
            _state.WaitingForUserInput = false;

            var getFileResult =
                await _state.Server.GetFileAsync(
                    remoteIp,
                    remotePort,
                    fileToGet,
                    _state.MyInfo.LocalIpAddress.ToString(),
                    _state.MyInfo.Port,
                    _state.Settings.TransferFolderPath,
                    _token)
                    .ConfigureAwait(false);

            return getFileResult.Success
                ? Result.Ok()
                : getFileResult;
        }

        Result<string> ChooseFileToGet()
        {
            var fileMenuChoice = 0;
            var totalMenuChoices = _state.FileInfoList.Count + 1;
            var returnToMainMenu = totalMenuChoices;

            while (fileMenuChoice == 0)
            {
                Console.WriteLine("Choose a file to download:");

                foreach (var i in Enumerable.Range(0, _state.FileInfoList.Count))
                {
                    var fileName = Path.GetFileName(_state.FileInfoList[i].filePath);
                    var fileSize = _state.FileInfoList[i].fileSize;
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
                : Result.Ok(_state.FileInfoList[fileMenuChoice - 1].filePath);
        }

        async void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            _log.Info(serverEvent.ToString());
            EventOccurred?.Invoke(this, serverEvent);

            switch (serverEvent.EventType)
            {
                case EventType.AcceptConnectionAttemptStarted:
                    _state.WaitingForServerToBeginAcceptingConnections = false;
                    return;

                case EventType.AcceptConnectionAttemptComplete:
                    await SaveNewClientAsync(serverEvent.RemoteServerIpAddress, serverEvent.RemoteServerPortNumber).ConfigureAwait(false);
                    return;

                case EventType.ReadTextMessageComplete:
                    HandleReadTextMessageComplete(serverEvent);
                    break;
                    
                case EventType.ReadInboundFileTransferInfoComplete:
                    _state.ClientInfo.SessionIpAddress = Network.ParseSingleIPv4Address(serverEvent.RemoteServerIpAddress).Value;
                    _state.ClientInfo.Port = serverEvent.RemoteServerPortNumber;
                    _state.DownloadFileName = serverEvent.FileName;
                    _state.RetryCounter = 0;
                    return;

                case EventType.ReceiveFileBytesStarted:
                    HandleReceiveFileBytesStarted(serverEvent);
                    return;

                case EventType.ReceiveFileBytesComplete:
                    await HandleReceiveFileBytesComplete(serverEvent);
                    break;
                    
                case EventType.ReceiveConfirmationMessageComplete:
                    _state.WaitingForConfirmationMessage = false;
                    break;

                case EventType.ReadFileListResponseComplete:
                    _state.WaitingForFileListResponse = false;
                    _state.FileInfoList = serverEvent.FileInfoList;
                    return;

                case EventType.SendFileTransferStalledComplete:
                    HandleFileTransferStalled();
                    break;

                case EventType.SendFileTransferRejectedComplete:
                    _state.WaitingForUserInput = true;
                    _signalExitRetryDownloadLogic.WaitOne();
                    break;

                case EventType.ReceiveFileTransferRejectedComplete:
                    _state.WaitingForConfirmationMessage = false;
                    _state.FileTransferRejected = true;
                    break;

                case EventType.ReceiveFileTransferStalledComplete:
                    _state.WaitingForConfirmationMessage = false;
                    _state.FileTransferCanceled = true;
                    break;

                case EventType.ReceiveNotificationNoFilesToDownloadComplete:
                    _state.WaitingForFileListResponse = false;
                    _state.NoFilesAvailableForDownload = true;
                    break;

                case EventType.ErrorOccurred:
                    _state.ErrorOccurred = true;
                    break;

                default:
                    return;
            }

            if (_state.WaitingForUserInput)
            {
                //await WriteMenuToScreenAsync().ConfigureAwait(false);
            }
        }

        void HandleFileTransferProgress(object sender, ServerEvent serverEvent)
        {
            _progress.BytesReceived = serverEvent.TotalFileBytesReceived;
            _progress.Report(serverEvent.PercentComplete);
        }

        void HandleReadTextMessageComplete(ServerEvent serverEvent)
        {
            var textIpAddress = Network.ParseSingleIPv4Address(serverEvent.RemoteServerIpAddress).Value;
            var textPort = serverEvent.RemoteServerPortNumber;
            _state.TextMessageEndPoint = new IPEndPoint(textIpAddress, textPort);
            //_activeTextSession = true;                  

            Console.WriteLine($"\n{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} says:");
            Console.WriteLine(serverEvent.TextMessage);

            _state.WaitingForUserInput = true;
        }

        void HandleReceiveFileBytesStarted(ServerEvent serverEvent)
        {
            //_statusChecker = new StatusChecker(1000);
            //_statusChecker.NoActivityEvent += HandleStalledFileTransfer;

            _progress = new ConsoleProgressBar
            {
                FileSizeInBytes = serverEvent.FileSizeInBytes,
                NumberOfBlocks = 15,
                StartBracket = "|",
                EndBracket = "|",
                CompletedBlock = "|",
                UncompletedBlock = "-",
                DisplayAnimation = false,
                DisplayLastRxTime = true,
                FileStalledInterval = TimeSpan.FromSeconds(10)
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

            _state.WaitingForDownloadToComplete = false;
            _state.RetryCounter = 0;
            _signalExitRetryDownloadLogic.Set();
        }
        
        async void HandleStalledFileTransfer(object sender, ProgressEventArgs eventArgs)
        {
            _state.FileStalledInfo = eventArgs;
            _state.FileTransferStalled = true;
            _state.WaitingForDownloadToComplete = false;

            _progress.Dispose();
            _progressBarInstantiated = false;

            var notifyStalledResult = 
            await NotifyClientThatFileTransferHasStalledAsync(
                _state.ClientInfo.SessionIpAddress.ToString(),
                _state.ClientInfo.Port,
                new CancellationToken());

            if (notifyStalledResult.Failure)
            {
                Console.WriteLine(notifyStalledResult.Error);
            }

            if (_state.RetryCounter >= _state.Settings.MaxDownloadAttempts)
            {
                var maxRetriesReached =
                    "Maximum # of attempts to complete stalled file transfer reached or exceeded " +
                    $"({_state.Settings.MaxDownloadAttempts} failed attempts for \"{_state.DownloadFileName}\")";

                Console.WriteLine(maxRetriesReached);

                var folder = _state.Settings.TransferFolderPath;
                var filePath1 = $"{folder}{Path.DirectorySeparatorChar}{_state.DownloadFileName}";
                var filePath2 = Path.Combine(folder, _state.DownloadFileName);

                FileHelper.DeleteFileIfAlreadyExists(filePath1);

                _signalExitRetryDownloadLogic.Set();
                return;
            }

            var userPrompt = $"Try again to download file \"{_state.DownloadFileName}\" from {_state.ClientInfo.SessionIpAddress}:{_state.ClientInfo.Port}?";
            if (ConsoleStatic.PromptUserYesOrNo(userPrompt))
            {
                _state.RetryCounter++;
                await _state.Server.RetryCanceledFileTransfer(
                    _state.ClientInfo.SessionIpAddress.ToString(),
                    _state.ClientInfo.Port,
                    new CancellationToken());
            }
        }

        async Task<Result> NotifyClientThatFileTransferHasStalledAsync(
            string ipAddress,
            int port,
            CancellationToken token)
        {
            var notifyCientResult =
                await _state.Server.SendNotificationFileTransferStalledAsync(
                        ipAddress,
                        port,
                        _state.MyInfo.LocalIpAddress.ToString(),
                        _state.MyInfo.Port,
                        token).ConfigureAwait(false);

            return notifyCientResult.Failure
                ? Result.Fail($"\nError occurred when notifying client that file transfer data is no longer being received:\n{notifyCientResult.Error}")
                : Result.Fail("File transfer canceled, data is no longer being received from client");
        }

        private void HandleFileTransferStalled()
        {
            var sinceLastActivity = DateTime.Now - _state.FileStalledInfo.LastDataReceived;
            Console.WriteLine($"\n\nFile transfer has stalled, {sinceLastActivity.ToFormattedString()} elapsed since last data received");
        }

        async Task<Result> SaveNewClientAsync(string clientIpAddress, int clientPort)
        {
            var connectionInfo = new ConnectionInfo { Port = clientPort };

            var clientIp = Network.ParseSingleIPv4Address(clientIpAddress).Value;
            connectionInfo.SessionIpAddress = clientIp;

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

            var requestServerInfoCommand = new RequestAdditionalInfoFromRemoteServerCommand(_state, newClient);
            var requestServerInfoResult = await requestServerInfoCommand.ExecuteAsync();

            if (requestServerInfoResult.Failure)
            {
                Console.WriteLine(requestServerInfoResult.Error);
                return Result.Fail(requestServerInfoResult.Error);
            }

            _state.ClientInfo = newClient.ConnectionInfo;
            _state.ClientTransferFolderPath = newClient.TransferFolder;

            return Result.Ok();
        }
    }
}
