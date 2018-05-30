// TODO: User can clear the list of archived event logs/requests
// TODO: Expose socket settings in config menu: buffer size, listen backlog size and socket timeout length
// TODO: Expose file transfer settings in config menu: update interval length, file stalled timeout, max retries

namespace ServerConsole
{
    using System;
    using System.IO;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    using AaronLuna.Common.Extensions;
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;
    using AaronLuna.ConsoleProgressBar;

    using Menus;
    using TplSockets;

    public class ServerApplication
    {
        const string SettingsFileName = "settings.xml";
        readonly int _displayMessageDelay = Constants.TwoSecondsInMilliseconds;

        readonly Logger _log = new Logger(typeof(ServerApplication));
        readonly string _settingsFilePath;
        readonly CancellationTokenSource _cts;
        readonly AppState _state;
        MainMenu _mainMenu;

        public ServerApplication()
        {
            _settingsFilePath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SettingsFileName}";
            _cts = new CancellationTokenSource();
            _state = new AppState();
            _state.MessageDisplayTime = _displayMessageDelay;
            _state.LocalServer.EventOccurred += HandleServerEventAsync;
            _state.LocalServer.FileTransferProgress += HandleFileTransferProgress;
        }

        public async Task<Result> RunAsync()
        {
            var initializeServerResult = await InitializeServerAsync();
            if (initializeServerResult.Failure)
            {
                return initializeServerResult;
            }

            var token = _cts.Token;
            var runServerResult = Result.Ok();

            try
            {
                var runServerTask =
                    Task.Run(() =>
                        _state.LocalServer.RunAsync(token),
                        token);

                while (!_state.LocalServer.IsListening) { }

                _mainMenu = new MainMenu(_state);
                var shutdownServerResult = await _mainMenu.ExecuteAsync();
                if (shutdownServerResult.Failure)
                {
                    Console.WriteLine($"Error: {shutdownServerResult.Error}");
                }

                if (runServerTask == await Task.WhenAny(
                    runServerTask,
                    Task.Delay(Constants.OneSecondInMilliseconds, token)).ConfigureAwait(false))
                {
                    runServerResult = await runServerTask;
                }
                else
                {
                    _cts.Cancel();
                }

                if (_state.RestartRequired)
                {
                    runServerResult = Result.Fail("Restarting server...");
                }
            }
            catch (AggregateException ex)
            {
                _log.Error("Error raised in method RunAsync", ex);
                Console.WriteLine("\nException messages:");
                foreach (var ie in ex.InnerExceptions)
                {
                    _log.Error("Error raised in method RunAsync", ie);

                    var exceptionDetails = 
                        $"\t{ie.GetType().Name}: {ie.Message}{Environment.NewLine}" +
                        $"{ie.StackTrace}";

                    Console.WriteLine(exceptionDetails);
                }
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method RunAsync", ex);

                var exceptionDetails =
                    $"{ex.Message} ({ex.GetType()}) raised in method ServerApplication.RunAsync" +
                    $"{Environment.NewLine}{ex.StackTrace}";

                Console.WriteLine(exceptionDetails);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Accept connection task canceled");
            }
            catch (IOException ex)
            {
                _log.Error("Error raised in method RunAsync", ex);

                var exceptionDetails =
                    $"{ex.Message} ({ex.GetType()}) raised in method ServerApplication.RunAsync" +
                    $"{Environment.NewLine}{ex.StackTrace}";

                Console.WriteLine(exceptionDetails);
            }
            catch (Exception ex)
            {
                _log.Error("Error raised in method RunAsync", ex);

                var exceptionDetails =
                    $"{ex.Message} ({ex.GetType()}) raised in method ServerApplication.RunAsync" +
                    $"{Environment.NewLine}{ex.StackTrace}";

                Console.WriteLine(exceptionDetails);
            }

            return runServerResult;
        }

        async Task<Result> InitializeServerAsync()
        {
            InitializeSettings();
            var settingsChanged = false;

            if (_state.Settings.LocalPort == 0)
            {
                _state.Settings.LocalPort =
                    SharedFunctions.GetPortNumberFromUser(
                        Resources.Prompt_SetLocalPortNumber,
                        true);

                settingsChanged = true;
            }

            if (string.IsNullOrEmpty(_state.Settings.LocalNetworkCidrIp))
            {
                var cidrIp = SharedFunctions.GetIpAddressFromUser(Resources.Prompt_SetLanCidrIp);
                var cidrNetworkBitCount = SharedFunctions.GetCidrIpNetworkBitCountFromUser();
                _state.Settings.LocalNetworkCidrIp = $"{cidrIp}/{cidrNetworkBitCount}";

                settingsChanged = true;
            }

            await _state.LocalServer.InitializeAsync(_state.Settings.LocalNetworkCidrIp, _state.Settings.LocalPort);
            _state.LocalServer.SocketSettings = _state.Settings.SocketSettings;
            _state.LocalServer.TransferUpdateInterval = _state.Settings.FileTransferUpdateInterval;
            _state.LocalServer.Info.TransferFolder = _state.Settings.LocalServerFolderPath;

            if (settingsChanged)
            {
                ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);
            }

            return Result.Ok();
        }

        void InitializeSettings()
        {
            _state.SettingsFile = new FileInfo(_settingsFilePath);

            var readSettingsFileResult = ServerSettings.ReadFromFile(_state.SettingsFilePath);
            if (readSettingsFileResult.Failure)
            {
                Console.WriteLine(readSettingsFileResult.Error);
            }

            _state.Settings = readSettingsFileResult.Value;
            _state.UserEntryLocalServerPort = _state.Settings.LocalPort;
            _state.UserEntryLocalNetworkCidrIp = _state.Settings.LocalNetworkCidrIp;
        }

        async void HandleServerEventAsync(object sender, ServerEvent serverEvent)
        {
            _log.Info(serverEvent.ToString());

            switch (serverEvent.EventType)
            {
                case EventType.ReceiveMessageFromClientComplete:
                    await ReceiveMessageFromClientCompleteAsync(serverEvent);
                    break;

                case EventType.ProcessRequestComplete:
                    await ProcessRequestCompleteAsync(serverEvent);
                    break;

                case EventType.ReceivedTextMessage:
                    await ReceivedTextMessageAsync(serverEvent);
                    break;

                case EventType.ReceivedServerInfo:
                    ReceivedServerInfo(serverEvent);
                    _state.WaitingForServerInfoResponse = false;
                    break;

                case EventType.ReceivedFileList:
                    _state.WaitingForFileListResponse = false;
                    return;

                case EventType.ReceivedNotificationNoFilesToDownload:
                    _state.NoFilesAvailableForDownload = true;
                    _state.WaitingForFileListResponse = false;
                    break;

                case EventType.ReceivedNotificationFolderDoesNotExist:
                    _state.RequestedFolderDoesNotExist = true;
                    _state.WaitingForFileListResponse = false;
                    break;

                case EventType.SendFileBytesStarted:
                    _state.FileTransferInProgress = true;
                    await _mainMenu.DisplayMenuAsync();
                    break;

                case EventType.FileTransferStalled:
                case EventType.ReceiveConfirmationMessageComplete:
                    _state.FileTransferInProgress = false;
                    await _mainMenu.DisplayMenuAsync();
                    break;

                case EventType.ReceivedInboundFileTransferRequest:
                    ReceivedInboundFileTransferRequest(serverEvent);
                    break;

                case EventType.ReceiveFileBytesStarted:
                    ReceiveFileBytesStarted(serverEvent);
                    break;

                case EventType.ReceiveFileBytesComplete:
                    await ReceiveFileBytesCompleteAsync(serverEvent);
                    break;

                case EventType.SendFileTransferRejectedStarted:
                    await RejectFileTransfer(serverEvent);
                    break;

                case EventType.SendFileTransferStalledComplete:
                    await NotifiedRemoteServerThatFileTransferIsStalledAsync(serverEvent);
                    break;
                    
                case EventType.ErrorOccurred:
                    _state.ErrorOccurred = true;
                    break;

                default:
                    return;
            }
        }
        
        async Task ReceiveMessageFromClientCompleteAsync(ServerEvent serverEvent)
        {
            if (_state.FileTransferInProgress) return;
            if (serverEvent.MessageType == MessageType.ShutdownServerCommand) return;
            if (serverEvent.Message.MustBeProcessedImmediately()) return;
            
            Console.WriteLine($"{Environment.NewLine}New {serverEvent.Message.Type.Name()} from {serverEvent.RemoteServerIpAddress}, added to queue");
            await Task.Delay(_state.MessageDisplayTime);
            await _mainMenu.DisplayMenuAsync();
        }

        async Task ProcessRequestCompleteAsync(ServerEvent serverEvent)
        {
            if (_state.FileTransferInProgress) return;
            if (DoNotDisplayRequestProcessedMessage(serverEvent.MessageType)) return;

            Console.WriteLine($"{Environment.NewLine}Processed {serverEvent.MessageType.Name()} from {serverEvent.RemoteServerIpAddress}, addded to archive");
            await Task.Delay(_state.MessageDisplayTime);
            await _mainMenu.DisplayMenuAsync();
        }

        bool DoNotDisplayRequestProcessedMessage(MessageType messageType)
        {
            switch (messageType)
            {
                case MessageType.TextMessage:
                case MessageType.RequestedFolderDoesNotExist:
                case MessageType.NoFilesAvailableForDownload:
                case MessageType.FileListResponse:
                case MessageType.FileTransferAccepted:
                case MessageType.InboundFileTransferRequest:
                case MessageType.ShutdownServerCommand:
                    return true;

                default:
                    return false;
            }
        }

        async Task ReceivedTextMessageAsync(ServerEvent serverEvent)
        {
            _state.SignalReturnToMainMenu.Set();
            Console.WriteLine($"{Environment.NewLine}{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} says:");
            Console.WriteLine(serverEvent.TextMessage);

            Console.WriteLine($"{Environment.NewLine}Processed {MessageType.InboundFileTransferRequest.Name()} from {serverEvent.RemoteServerIpAddress}, addded to archive");
            await Task.Delay(_state.MessageDisplayTime);
            _state.SignalReturnToMainMenu.WaitOne();
        }

        async Task RejectFileTransfer(ServerEvent serverEvent)
        {
            var fileAlreadyExists =
                $"{Environment.NewLine}A file with the same name already exists in the " +
                "download folder, please rename or remove this file in order to proceed";

            _state.SignalReturnToMainMenu.Set();
            Console.WriteLine(fileAlreadyExists);

            Console.WriteLine($"{Environment.NewLine}Processed {MessageType.InboundFileTransferRequest.Name()} from {serverEvent.RemoteServerIpAddress}, addded to archive");
            await Task.Delay(_state.MessageDisplayTime);
            _state.SignalReturnToMainMenu.WaitOne();
        }

        void ReceivedServerInfo(ServerEvent serverEvent)
        {
            _state.SelectedServer.TransferFolder = serverEvent.RemoteFolder;
            _state.SelectedServer.PublicIpAddress = serverEvent.PublicIpAddress;
            _state.SelectedServer.LocalIpAddress = serverEvent.LocalIpAddress;

            var remoteServerLocalIp = serverEvent.LocalIpAddress;
            var cidrIp = _state.Settings.LocalNetworkCidrIp;
            var addressInRangeCheck = NetworkUtilities.IpAddressIsInRange(remoteServerLocalIp, cidrIp);

            if (addressInRangeCheck.Success && addressInRangeCheck.Value)
            {
                _state.SelectedServer.SessionIpAddress = serverEvent.LocalIpAddress;
            }
            else
            {
                _state.SelectedServer.SessionIpAddress = serverEvent.PublicIpAddress;
            }
        }

        void ReceivedInboundFileTransferRequest(ServerEvent serverEvent)
        {
            if (_state.RetryLimitExceeded && !_state.FileTransferStalled)
            {
                _state.RetryCounter = 0;
            }

            var fileTransferSender =
                $"{Environment.NewLine}Incoming file transfer from " +
                $"{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}:" +
                $"{Environment.NewLine}";

            var retryCounter = _state.RetryCounter > 0
                ? $"Attempt #{_state.RetryCounter}/{_state.Settings.MaxDownloadAttempts}" +
                  Environment.NewLine
                : string.Empty;

            var fileTransferDetails =
                $"File Name:\t{serverEvent.FileName}{Environment.NewLine}" +
                $"File Size:\t{serverEvent.FileSizeString}{Environment.NewLine}" +
                $"Save To:\t{serverEvent.LocalFolder}{Environment.NewLine}";

            Console.WriteLine($"{fileTransferSender}{retryCounter}");
            Console.WriteLine(fileTransferDetails);
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
        }

        void HandleFileTransferProgress(object sender, ServerEvent serverEvent)
        {
            _state.ProgressBar.BytesReceived = serverEvent.TotalFileBytesReceived;
            _state.ProgressBar.Report(serverEvent.PercentComplete);
        }

        async Task ReceiveFileBytesCompleteAsync(ServerEvent serverEvent)
        {
            _state.ProgressBar.BytesReceived = serverEvent.FileSizeInBytes;
            _state.ProgressBar.Report(1);
            _state.SignalReturnToMainMenu.WaitOne(Constants.OneHalfSecondInMilliseconds);

            var report =
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"Download Started:\t{serverEvent.FileTransferStartTime:MM/dd/yyyy HH:mm:ss.fff}{Environment.NewLine}" +
                $"Download Finished:\t{serverEvent.FileTransferCompleteTime:MM/dd/yyyy HH:mm:ss.fff}{Environment.NewLine}" +
                $"Elapsed Time:\t\t{serverEvent.FileTransferElapsedTimeString}{Environment.NewLine}" +
                $"Transfer Rate:\t\t{serverEvent.FileTransferRate}";
            
            Console.WriteLine(report);
            _state.SignalReturnToMainMenu.Set();
            Console.WriteLine($"{Environment.NewLine}Processed {MessageType.InboundFileTransferRequest.Name()} from {serverEvent.RemoteServerIpAddress}, addded to archive");

            _state.ProgressBar.Dispose();
            _state.ProgressBarInstantiated = false;
            _state.RetryCounter = 0;

            await Task.Delay(_state.MessageDisplayTime);
            _state.SignalReturnToMainMenu.WaitOne();
        }

        async void HandleStalledFileTransferAsync(object sender, ProgressEventArgs eventArgs)
        {
            _state.FileStalledInfo = eventArgs;
            _state.ProgressBar.Dispose();
            _state.ProgressBarInstantiated = false;

            FileHelper.DeleteFileIfAlreadyExists(_state.LocalServer.IncomingFilePath);

            var notifyStalledResult = await SendFileTransferStalledNotification();
            if (notifyStalledResult.Failure)
            {
                Console.WriteLine(notifyStalledResult.Error);
            }
        }

        async Task<Result> SendFileTransferStalledNotification()
        {
            var fileStalledMessage =
                $"{Environment.NewLine}{Environment.NewLine}Data is no longer being received " +
                "from remote server, attempting to cancel file transfer...";

            Console.WriteLine(fileStalledMessage);

            var notifyClientResult = await _state.LocalServer.SendNotificationFileTransferStalledAsync();

            var notifyClientError =
                $"{Environment.NewLine}Error occurred when notifying client that file transfer data " +
                $"is no longer being received:\n{notifyClientResult.Error}";

            return notifyClientResult.Success
                ? Result.Ok()
                : Result.Fail(notifyClientError);
        }
        
        async Task NotifiedRemoteServerThatFileTransferIsStalledAsync(ServerEvent serverEvent)
        {
            var sinceLastActivity = DateTime.Now - _state.FileStalledInfo.LastDataReceived;

            var sentFileStalledNotification =
                $"{Environment.NewLine}Successfully notified remote server that file transfer has " +
                $"stalled, {sinceLastActivity.ToFormattedString()} elapsed since last data received.";

            Console.WriteLine(sentFileStalledNotification);

            if (_state.RetryLimitExceeded)
            {
                var maxRetriesReached =
                    $"{Environment.NewLine}Maximum # of attempts to complete stalled file transfer reached or exceeded: " +
                    $"({_state.Settings.MaxDownloadAttempts} failed attempts for \"{_state.IncomingFileName}\")";

                Console.WriteLine(maxRetriesReached);
            }

            Console.WriteLine($"{Environment.NewLine}Processed {MessageType.InboundFileTransferRequest.Name()} from {serverEvent.RemoteServerIpAddress}, addded to archive");
            _state.SignalReturnToMainMenu.Set();

            await Task.Delay(_state.MessageDisplayTime);
            _state.SignalReturnToMainMenu.WaitOne();
        }
    }
}
