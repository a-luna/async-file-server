namespace ServerConsole.Commands.Menus
{
    using System;
    using System.IO;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Extensions;
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;
    using AaronLuna.ConsoleProgressBar;

    using ServerCommands;

    using TplSockets;

    class MainMenu : SelectionMenuLoop
    {
        readonly AppState _state;
        readonly Logger _log = new Logger(typeof(MainMenu));

        public MainMenu(AppState state)
        {
            ReturnToParent = true;
            ItemText = "Main menu";
            MenuText = "\nMenu for TPL socket server:";
            Options = new List<ICommand>();

            var selectServerCommand = new SelectRemoteServerMenu(state);
            var selectActionCommand = new SelectServerActionMenu(state);
            var changeSettingsCommand = new ChangeSettingsMenu(state);
            var shutdownCommand = new ShutdownServerCommand();

            Options.Add(selectServerCommand);
            Options.Add(selectActionCommand);
            Options.Add(changeSettingsCommand);
            Options.Add(shutdownCommand);

            _state = state;
            _state.LocalServer.EventOccurred += HandleServerEventAsync;
            _state.LocalServer.FileTransferProgress += HandleFileTransferProgress;
        }

        public new async Task<Result> ExecuteAsync()
        {
            var exit = false;
            Result result = null;

            while (!exit)
            {
                var userSelection = 0;
                while (userSelection == 0)
                {
                    MenuFunctions.DisplayMenu(MenuText, Options);
                    var input = Console.ReadLine();

                    var validationResult = MenuFunctions.ValidateUserInput(input, OptionCount);
                    if (validationResult.Failure)
                    {
                        _log.Error($"Error: {validationResult.Error} (MainMenu.ExecuteAsync)");
                        Console.WriteLine(validationResult.Error);
                        continue;
                    }

                    userSelection = validationResult.Value;
                }

                var selectedOption = Options[userSelection - 1];
                result = await selectedOption.ExecuteAsync();
                exit = selectedOption.ReturnToParent;

                if (result.Success) continue;
                Console.WriteLine($"{Environment.NewLine}Error: {result.Error}");

                if (result.Error.Contains(SharedFunctions.NoClientSelectedError))
                {
                    Console.WriteLine("Press Enter to return to main menu.");
                    Console.ReadLine();
                    continue;
                }

                _log.Error($"Error: {result.Error} (MainMenu.ExecuteAsync)");
                exit = SharedFunctions.PromptUserYesOrNo("Exit program?");
            }
            
            return result;
        }

        async void HandleServerEventAsync(object sender, ServerEvent serverEvent)
        {
            _log.Info(serverEvent.ToString());
            DisplayServerEvent(serverEvent);
            await ProcessServerEventAsync(serverEvent);

            Console.Clear();
            MenuFunctions.DisplayMenu(MenuText, Options);
        }

        void HandleFileTransferProgress(object sender, ServerEvent serverEvent)
        {
            _state.ProgressBar.BytesReceived = serverEvent.TotalFileBytesReceived;
            _state.ProgressBar.Report(serverEvent.PercentComplete);
        }

        void DisplayServerEvent(ServerEvent serverEvent)
        {
            string fileCount;

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

                //case EventType.SendFileTransferCanceledStarted:
                //case EventType.ReceiveFileTransferCanceledComplete:
                //    Console.WriteLine("File transfer successfully canceled");
                //    break;

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
                    _state.FileInfoList = serverEvent.RemoteServerFileList;
                    return;

                case EventType.SendFileTransferStalledComplete:
                    FileTransferStalled();
                    break;

                case EventType.SendFileTransferRejectedComplete:
                    _state.SignalExitRetryDownloadLogic.WaitOne();
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
            _state.SignalExitRetryDownloadLogic.Set();
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

                _state.SignalExitRetryDownloadLogic.Set();
                return;
            }

            var userPrompt = $"Try again to download file \"{_state.IncomingFileName}\" from {_state.RemoteServerInfo.SessionIpAddress}:{_state.RemoteServerInfo.Port}?";
            if (SharedFunctions.PromptUserYesOrNo(userPrompt))
            {
                _state.RetryCounter++;
                await _state.LocalServer.RetryLastFileTransferAsync(
                    _state.ClientSessionIpAddress,
                    _state.ClientServerPort);
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
    }
}
 