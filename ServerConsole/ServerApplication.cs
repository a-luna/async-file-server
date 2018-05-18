namespace ServerConsole
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    using AaronLuna.Common.Extensions;
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;
    using AaronLuna.ConsoleProgressBar;

    using Commands.CompositeCommands;
    using Commands.Menus;

    using TplSockets;

    public class ServerApplication
    {
        const string SettingsFileName = "settings.xml";
        readonly Logger _log = new Logger(typeof(ServerApplication));
        readonly string _settingsFilePath;
        readonly CancellationTokenSource _cts;
        readonly AppState _state;

        public ServerApplication()
        {
            _settingsFilePath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SettingsFileName}";
            _cts = new CancellationTokenSource();
            _state = new AppState();
            _state.LocalServer.EventOccurred += HandleServerEventAsync;
            _state.LocalServer.FileTransferProgress += HandleFileTransferProgress;
        }

        public async Task<Result> RunAsync()
        {
            var initializeServerCommand = new InitializeServerCommand(_state, _settingsFilePath);
            var initializeServerResult = await initializeServerCommand.ExecuteAsync();
            if (initializeServerResult.Failure)
            {
                return initializeServerResult;
            }
            
            var token = _cts.Token;
            var result = Result.Fail(string.Empty);

            try
            {
                var runServerTask =
                    Task.Run(() =>
                        _state.LocalServer.RunAsync(),
                        token);

                while (!_state.LocalServer.IsListening) { }

                var mainMenu = new MainMenu(_state);
                result = await mainMenu.ExecuteAsync();

                if (_state.ProgressBarInstantiated)
                {
                    _state.ProgressBar.Dispose();
                    _state.ProgressBarInstantiated = false;
                }

                var shutdownTask = Task.Factory.StartNew(() => _state.LocalServer.ShutdownAsync(), token);
                await Task.WhenAll(shutdownTask, runServerTask);
                result = await runServerTask;
            }
            catch (AggregateException ex)
            {
                _log.Error("Error raised in method RunAsync", ex);
                Console.WriteLine("\nException messages:");
                foreach (var ie in ex.InnerExceptions)
                {
                    _log.Error("Error raised in method RunAsync", ie);
                    Console.WriteLine($"\t{ie.GetType().Name}: {ie.Message}");
                }
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method RunAsync", ex);
                Console.WriteLine($"{ex.Message} ({ex.GetType()}) raised in method ServerApplication.RunAsync");
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Accept connection task canceled");
            }
            catch (Exception ex)
            {
                _log.Error("Error raised in method RunAsync", ex);
                Console.WriteLine($"{ex.Message} ({ex.GetType()}) raised in method ServerApplication.RunAsync");
            }

            return result;
        }

        async void HandleServerEventAsync(object sender, ServerEvent serverEvent)
        {

            _log.Info(serverEvent.ToString());
            DisplayServerEvent(serverEvent);
            await ProcessServerEventAsync(serverEvent);

            Console.Clear();
            //Menu.DisplayMenu(MenuText, MenuOptions);
        }

        void HandleFileTransferProgress(object sender, ServerEvent serverEvent)
        {
            _state.ProgressBar.BytesReceived = serverEvent.TotalFileBytesReceived;
            _state.ProgressBar.Report(serverEvent.PercentComplete);
        }

        void DisplayServerEvent(ServerEvent serverEvent)
        {
            switch (serverEvent.EventType)
            {
                case EventType.ReceivedOutboundFileTransferRequest:
                    Console.WriteLine("\nReceived outbound file transfer request");
                    Console.WriteLine($"File Requested:\t\t{serverEvent.FileName}\nFile Size:\t\t{serverEvent.FileSizeString}\nRemote Endpoint:\t{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\nTarget Directory:\t{serverEvent.RemoteFolder}");
                    break;

                case EventType.ReceivedInboundFileTransferRequest:
                    Console.WriteLine($"\nIncoming file transfer from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}:");
                    Console.WriteLine($"File Name:\t{serverEvent.FileName}\nFile Size:\t{serverEvent.FileSizeString}\nSave To:\t{serverEvent.LocalFolder}");
                    break;

                case EventType.SendNotificationNoFilesToDownloadStarted:
                    Console.WriteLine($"\nClient ({serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}) requested list of files available to download, but transfer folder is empty");
                    break;

                case EventType.ReceivedNotificationNoFilesToDownload:
                    Console.WriteLine("\nClient has no files available for download.");
                    break;

                case EventType.SendFileTransferRejectedStarted:
                    Console.WriteLine("\nA file with the same name already exists in the download folder, please rename or remove this file in order to proceed");
                    break;

                case EventType.SendFileBytesStarted:
                    Console.WriteLine("\nSending file to client...");
                    break;

                case EventType.ReceiveConfirmationMessageComplete:
                    Console.WriteLine("Client confirmed file transfer completed successfully");
                    break;

                case EventType.RequestFileListStarted:
                    Console.WriteLine($"Sending request for list of available files to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}...");
                    break;

                case EventType.ReceivedFileListRequest:
                    Console.WriteLine($"\nReceived request for list of available files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}...");
                    break;

                case EventType.SendFileListStarted:
                    Console.WriteLine($"Sending list of files to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} (File count: {serverEvent.RemoteServerFileList.Count})");
                    break;

                case EventType.ReceivedFileList:
                    Console.WriteLine($"Received list of files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} (File count: {serverEvent.RemoteServerFileList.Count})\n");
                    break;

                case EventType.RequestPublicIpAddressStarted:
                    Console.WriteLine($"\nSending request for public IP address to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}...");
                    break;

                case EventType.ReceivedPublicIpAddressRequest:
                    Console.WriteLine($"\nReceived request for public IP address from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}...");
                    break;

                case EventType.RequestTransferFolderPathStarted:
                    Console.WriteLine($"Sending request for transfer folder path to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}...");
                    break;

                case EventType.ReceivedTransferFolderPathRequest:
                    Console.WriteLine($"\nReceived request for transfer folder path from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}...");
                    break;

                case EventType.SendTransferFolderPathStarted:
                case EventType.SendPublicIpAddressStarted:
                    Console.Write("Sent");
                    break;

                case EventType.ReceivedTransferFolderPath:
                case EventType.ReceivedPublicIpAddress:
                    Console.Write("Success");
                    break;

                case EventType.ShutdownListenSocketCompletedWithoutError:
                    Console.WriteLine("Server has been successfully shutdown, press Enter to exit program\n");
                    break;

                case EventType.ShutdownListenSocketCompletedWithError:
                    Console.WriteLine($"Error occurred while attempting to shutdown listening socket:{Environment.NewLine}{serverEvent.ErrorMessage}");
                    break;

                case EventType.ErrorOccurred:
                    Console.WriteLine($"Error: {serverEvent.ErrorMessage}");
                    break;
            }
        }

        async Task ProcessServerEventAsync(ServerEvent serverEvent)
        {
            switch (serverEvent.EventType)
            {
                case EventType.ServerStartedListening:
                    _state.WaitingForServerToBeginAcceptingConnections = false;
                    return;

                case EventType.ReceivedTextMessage:
                    ReceivedTextMessageComplete(serverEvent);
                    break;

                case EventType.ReceivedTransferFolderPath:
                    _state.WaitingForTransferFolderResponse = false;
                    break;

                case EventType.ReceivedPublicIpAddress:
                    _state.WaitingForPublicIpResponse = false;
                    break;

                case EventType.ReceiveFileBytesStarted:
                    ReceiveFileBytesStarted(serverEvent);
                    return;

                case EventType.ReceiveFileBytesComplete:
                    await ReceiveFileBytesCompleteAsync(serverEvent);
                    break;

                case EventType.ReceiveConfirmationMessageComplete:
                    _state.WaitingForConfirmationMessage = false;
                    break;

                case EventType.ReceivedFileList:
                    _state.WaitingForFileListResponse = false;
                    return;

                case EventType.SendFileTransferStalledComplete:
                    FileTransferStalled();
                    break;

                case EventType.SendFileTransferRejectedComplete:
                    _state.SignalRetryLimitExceeded.WaitOne();
                    break;

                case EventType.ClientRejectedFileTransfer:
                    _state.FileTransferRejected = true;
                    _state.WaitingForConfirmationMessage = false;
                    break;

                case EventType.FileTransferStalled:
                    _state.FileTransferCanceled = true;
                    _state.WaitingForConfirmationMessage = false;
                    break;

                case EventType.ReceivedNotificationNoFilesToDownload:
                    _state.NoFilesAvailableForDownload = true;
                    _state.WaitingForFileListResponse = false;
                    break;

                case EventType.ErrorOccurred:
                    _state.ErrorOccurred = true;
                    break;

                default:
                    return;
            }
        }

        void ReceivedTextMessageComplete(ServerEvent serverEvent)
        {
            Console.Clear();
            Console.WriteLine($"\n{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} says:");
            Console.WriteLine(serverEvent.TextMessage);

            Console.WriteLine("Presss enter to return to main menu");
            Console.ReadLine();
            Console.WriteLine("Returning to main menu...");

            //if (SharedFunctions.PromptUserYesOrNo($"{Environment.NewLine}Reply to {textIp}:{textPort}?"))
            //{
            //    var message = Console.ReadLine();

            //    var sendMessageResult =
            //        await _state.LocalServer.SendTextMessageAsync(
            //            message,
            //            textIp.ToString(),
            //            textPort,
            //            _state.MyLocalIpAddress,
            //            _state.MyServerPort,
            //            new CancellationToken()).ConfigureAwait(false);

            //    if (sendMessageResult.Failure)
            //    {
            //        Console.WriteLine(sendMessageResult.Error);
            //    }
            //}

            //_state.WaitingForUserInput = true;
            //_state.SignalDispayMenu.Set();
        }

        void ReceiveFileBytesStarted(ServerEvent serverEvent)
        {
            _state.ProgressBar =
                new FileTransferProgressBar(serverEvent.FileSizeInBytes, TimeSpan.FromSeconds(5))
                {
                    NumberOfBlocks = 15,
                    StartBracket = "|",
                    EndBracket = "|",
                    CompletedBlock = "|",
                    IncompleteBlock = " ",
                    DisplayAnimation = false
                };

            _state.ProgressBar.FileTransferStalled += HandleStalledFileTransferAsync;
            _state.ProgressBarInstantiated = true;
            Console.WriteLine(Environment.NewLine);
        }

        async Task ReceiveFileBytesCompleteAsync(ServerEvent serverEvent)
        {
            _state.ProgressBar.BytesReceived = serverEvent.FileSizeInBytes;
            _state.ProgressBar.Report(1);
            await Task.Delay(SharedFunctions.OneHalfSecondInMilliseconds);

            _state.ProgressBar.Dispose();
            _state.ProgressBarInstantiated = false;

            Console.WriteLine($"\n\nTransfer Start Time:\t\t{serverEvent.FileTransferStartTime.ToLongTimeString()}");
            Console.WriteLine($"Transfer Complete Time:\t\t{serverEvent.FileTransferCompleteTime.ToLongTimeString()}");
            Console.WriteLine($"Elapsed Time:\t\t\t{serverEvent.FileTransferElapsedTimeString}");
            Console.WriteLine($"Transfer Rate:\t\t\t{serverEvent.FileTransferRate}");

            _state.WaitingForDownloadToComplete = false;
            _state.RetryCounter = 0;
            _state.SignalRetryLimitExceeded.Set();
        }

        async void HandleStalledFileTransferAsync(object sender, ProgressEventArgs eventArgs)
        {
            _state.FileStalledInfo = eventArgs;
            _state.FileTransferStalled = true;
            _state.WaitingForDownloadToComplete = false;

            _state.ProgressBar.Dispose();
            _state.ProgressBarInstantiated = false;

            var notifyStalledResult = await NotifyClientThatFileTransferHasStalled();
            if (notifyStalledResult.Failure)
            {
                Console.WriteLine(notifyStalledResult.Error);
            }

            if (_state.RetryCounter >= _state.Settings.MaxDownloadAttempts)
            {
                _state.RetryCounter = 0;

                var maxRetriesReached =
                    "Maximum # of attempts to complete stalled file transfer reached or exceeded " +
                    $"({_state.Settings.MaxDownloadAttempts} failed attempts for \"{_state.IncomingFileName}\")";

                Console.WriteLine(maxRetriesReached);

                var folder = _state.Settings.LocalServerFolderPath;
                var filePath1 = $"{folder}{Path.DirectorySeparatorChar}{_state.IncomingFileName}";
                var filePath2 = Path.Combine(folder, _state.IncomingFileName);

                FileHelper.DeleteFileIfAlreadyExists(filePath1);

                _state.SignalRetryLimitExceeded.Set();
                return;
            }

            var userPrompt = $"Try again to download file \"{_state.IncomingFileName}\" from {_state.RemoteServerInfo.SessionIpAddress}:{_state.RemoteServerInfo.Port}?";
            if (SharedFunctions.PromptUserYesOrNo(userPrompt))
            {
                _state.RetryCounter++;
                await _state.LocalServer.RetryLastFileTransferAsync(
                    _state.RemoteServerInfo.SessionIpAddress,
                    _state.RemoteServerInfo.Port);
            }
        }

        async Task<Result> NotifyClientThatFileTransferHasStalled()
        {
            var notifyCientResult = await _state.LocalServer.SendNotificationFileTransferStalledAsync();

            return notifyCientResult.Failure
                ? Result.Fail($"\nError occurred when notifying client that file transfer data is no longer being received:\n{notifyCientResult.Error}")
                : Result.Fail("File transfer canceled, data is no longer being received from client");

        }

        void FileTransferStalled()
        {
            var sinceLastActivity = DateTime.Now - _state.FileStalledInfo.LastDataReceived;
            Console.WriteLine($"\n\nFile transfer has stalled, {sinceLastActivity.ToFormattedString()} elapsed since last data received");
        }

        async Task<Result> SendFileToClientAsync(IPAddress ipAddress, int port, string transferFolderPath, CancellationToken token)
        {
            var selectFileResult = OldStatic.ChooseFileToSend(_state.Settings.LocalServerFolderPath);
            if (selectFileResult.Failure)
            {
                Console.WriteLine(selectFileResult.Error);
                return Result.Ok();
            }

            var fileToSend = selectFileResult.Value;

            _state.WaitingForConfirmationMessage = true;
            _state.FileTransferRejected = false;
            _state.FileTransferCanceled = false;

            var sendFileResult =
                await _state.LocalServer.SendFileAsync(
                    ipAddress,
                    port,
                    fileToSend,
                    transferFolderPath);

            if (sendFileResult.Failure)
            {
                return sendFileResult;
            }

            while (_state.WaitingForConfirmationMessage) {}
            if (_state.FileTransferRejected)
            {
                return Result.Fail(Resources.Error_FileAlreadyExists);
            }

            if (_state.FileTransferCanceled)
            {
                return Result.Fail(Resources.Error_FileTransferCancelled);
            }

            return Result.Ok();
        }

        async Task<Result> RequestFileListFromClientAsync(string ipAddress, int port, string remoteFolder, CancellationToken token)
        {
            _state.WaitingForFileListResponse = true;
            _state.NoFilesAvailableForDownload = false;

            var requestFileListResult =
                await _state.LocalServer.RequestFileListAsync(
                        ipAddress,
                        port,
                        remoteFolder)
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

            var getFileResult =
                await _state.LocalServer.GetFileAsync(
                    remoteIp,
                    remotePort,
                    fileToGet,
                    _state.Settings.LocalServerFolderPath).ConfigureAwait(false);

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

                var validationResult = SharedFunctions.ValidateNumberIsWithinRange(input, 1, totalMenuChoices);
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
    }
}
