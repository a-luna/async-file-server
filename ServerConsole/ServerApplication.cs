// TODO: Expose file transfer settings in config menu: update interval length, file stalled timeout, max retries
// TODO: Investigate bug where if 2 file requests are in queue, all file transfers fail after the first transfer is processed. This does not occur when processing one file request at a time.

using AaronLuna.Common.IO;

namespace ServerConsole
{
    using System;
    using System.IO;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    using AaronLuna.Common.Extensions;
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
        TimeSpan _transferTimeout;
        MainMenu _mainMenu;
        int _inboundFileTransferId;

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

            if (_state.Settings.LocalServerPortNumber == 0)
            {
                _state.Settings.LocalServerPortNumber =
                    SharedFunctions.GetPortNumberFromUser(
                        Resources.Prompt_SetLocalPortNumber,
                        true);

                settingsChanged = true;
            }

            if (string.IsNullOrEmpty(_state.Settings.LocalNetworkCidrIp))
            {
                _state.Settings.LocalNetworkCidrIp = SharedFunctions.InitializeLanCidrIp();
                settingsChanged = true;
            }

            await _state.LocalServer.InitializeAsync(_state.Settings.LocalNetworkCidrIp, _state.Settings.LocalServerPortNumber);
            _state.LocalServer.SocketSettings = _state.Settings.SocketSettings;
            _state.LocalServer.TransferUpdateInterval = _state.Settings.TransferUpdateInterval;
            _state.LocalServer.TransferRetryLimit = _state.Settings.TransferRetryLimit;
            _state.LocalServer.RetryLimitLockout = _state.Settings.RetryLimitLockout;
            _state.LocalServer.Info.TransferFolder = _state.Settings.LocalServerFolderPath;

            _transferTimeout = _state.Settings.FileTransferStalledTimeout;

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
            _state.UserEntryLocalServerPort = _state.Settings.LocalServerPortNumber;
            _state.UserEntryLocalNetworkCidrIp = _state.Settings.LocalNetworkCidrIp;
        }

        async void HandleServerEventAsync(object sender, ServerEvent serverEvent)
        {
            _log.Info(serverEvent.ToString());

            switch (serverEvent.EventType)
            {
                case EventType.ReceiveMessageFromClientComplete:
                    ReceiveMessageFromClientComplete(serverEvent);
                    break;

                case EventType.ProcessRequestComplete:
                    ProcessRequestComplete(serverEvent);
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
                    _mainMenu.DisplayMenu();
                    break;

                case EventType.FileTransferStalled:
                case EventType.SendFileBytesComplete:
                    _state.FileTransferInProgress = false;
                    _mainMenu.DisplayMenu();
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
                    await RejectFileTransferAsync();
                    break;

                case EventType.SendFileTransferStalledComplete:
                    await NotifiedRemoteServerThatFileTransferIsStalledAsync();
                    break;

                case EventType.ReceiveRetryLimitExceeded:
                    await HandleRetryLimitExceededAsync(serverEvent);
                    break;
                    
                case EventType.ErrorOccurred:
                    _state.ErrorOccurred = true;
                    _state.ErrorMessage = serverEvent.ErrorMessage;
                    _state.FileTransferInProgress = false;

                    if (!_state.ProgressBarInstantiated) return;

                    _state.ProgressBar.Dispose();
                    _state.ProgressBarInstantiated = false;
                    break;

                default:
                    return;
            }
        }
        
        void ReceiveMessageFromClientComplete(ServerEvent serverEvent)
        {
            if (_state.FileTransferInProgress) return;
            if (serverEvent.RequestType == RequestType.ShutdownServerCommand) return;
            if (serverEvent.RequestType.ProcessRequestImmediately()) return;

            Thread.Sleep(Constants.OneHalfSecondInMilliseconds);
            _mainMenu.DisplayMenu();
        }

        void ProcessRequestComplete(ServerEvent serverEvent)
        {
            if (_state.FileTransferInProgress) return;
            if (DoNotDisplayRequestProcessedMessage(serverEvent.RequestType)) return;
            
            Thread.Sleep(Constants.OneHalfSecondInMilliseconds);
            _mainMenu.DisplayMenu();
        }

        bool DoNotDisplayRequestProcessedMessage(RequestType messageType)
        {
            switch (messageType)
            {
                case RequestType.TextMessage:
                case RequestType.RequestedFolderDoesNotExist:
                case RequestType.NoFilesAvailableForDownload:
                case RequestType.FileListRequest:
                case RequestType.FileListResponse:
                case RequestType.FileTransferAccepted:
                case RequestType.InboundFileTransferRequest:
                case RequestType.ShutdownServerCommand:
                    return true;

                default:
                    return false;
            }
        }

        async Task ReceivedTextMessageAsync(ServerEvent serverEvent)
        {
            Console.WriteLine($"{Environment.NewLine}{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} says:");
            Console.WriteLine(serverEvent.TextMessage);
            _state.SignalReturnToMainMenu.Set();
            
            await Task.Delay(Constants.OneHalfSecondInMilliseconds);
            _state.SignalReturnToMainMenu.WaitOne();
        }

        async Task RejectFileTransferAsync()
        {
            var fileAlreadyExists =
                $"{Environment.NewLine}A file with the same name already exists in the " +
                "download folder, please rename or remove this file in order to proceed";

            Console.WriteLine(fileAlreadyExists);
            _state.SignalReturnToMainMenu.Set();
            
            await Task.Delay(Constants.OneHalfSecondInMilliseconds);
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
            _inboundFileTransferId = serverEvent.FileTransferId;
            var remoteServerIp = serverEvent.RemoteServerIpAddress;
            var remotePortNumber = serverEvent.RemoteServerPortNumber;
            var retryCounter = serverEvent.RetryCounter;
            var remoteServerRetryLimit = serverEvent.RemoteServerRetryLimit;
            var fileName = serverEvent.FileName;
            var fileSize = FileHelper.FileSizeToString(serverEvent.FileSizeInBytes);
            var localFolder = serverEvent.LocalFolder;
            
            var retryLimit = remoteServerRetryLimit == 0
                ? string.Empty
                : $"/{remoteServerRetryLimit}{Environment.NewLine}";

            var transferAttempt = $"Attempt #{retryCounter}{retryLimit}";

            var remoteServerInfo =
                $"{Environment.NewLine}Incoming file transfer from " +
                $"{remoteServerIp}:{remotePortNumber} ({transferAttempt}){Environment.NewLine}";

            var fileInfo =
                $"File Name:\t{fileName}{Environment.NewLine}" +
                $"File Size:\t{fileSize}{Environment.NewLine}" +
                $"Save To:\t{localFolder}{Environment.NewLine}";
            
            Console.WriteLine(remoteServerInfo);
            Console.WriteLine(fileInfo);
        }

        void ReceiveFileBytesStarted(ServerEvent serverEvent)
        {
            var fileSize = serverEvent.FileSizeInBytes;

            _state.ProgressBar =
                new FileTransferProgressBar(fileSize, _transferTimeout)
                {
                    NumberOfBlocks = 20,
                    StartBracket = " |",
                    EndBracket = "|",
                    CompletedBlock = "|",
                    IncompleteBlock = "\u00a0",
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
            var fileSize = serverEvent.FileSizeInBytes;
            var startTime = serverEvent.FileTransferStartTime;
            var completeTime = serverEvent.FileTransferCompleteTime;
            var timeElapsed = (completeTime - startTime).ToFormattedString();
            var transferRate = FileTransfer.GetTransferRate(completeTime - startTime, fileSize);

            _state.ProgressBar.BytesReceived = fileSize;
            _state.ProgressBar.Report(1);
            _state.SignalReturnToMainMenu.WaitOne(Constants.OneHalfSecondInMilliseconds);
            
            var report =
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"Download Started:\t{startTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                $"Download Finished:\t{completeTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                $"Elapsed Time:\t\t{timeElapsed}{Environment.NewLine}" +
                $"Transfer Rate:\t\t{transferRate}";
            
            Console.WriteLine(report);
            _state.SignalReturnToMainMenu.Set();

            _state.ProgressBar.Dispose();
            _state.ProgressBarInstantiated = false;
            _state.RetryCounter = 0;

            await Task.Delay(Constants.OneHalfSecondInMilliseconds);
            _state.SignalReturnToMainMenu.WaitOne();
        }

        async void HandleStalledFileTransferAsync(object sender, ProgressEventArgs eventArgs)
        {
            var getFileTransferResult = _state.LocalServer.GetFileTransferById(_state.InboundFileTransferId);
            if (getFileTransferResult.Failure)
            {
                var error = $"{getFileTransferResult.Error} (ServerApplication.HandleStalledFileTransferAsync)";
                Console.WriteLine(error);
                return;
            }

            var inboundFileTransfer = getFileTransferResult.Value;

            _state.FileStalledInfo = eventArgs;
            _state.ProgressBar.Dispose();
            _state.ProgressBarInstantiated = false;
            
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

            var notifyClientResult = 
                await _state.LocalServer.SendNotificationFileTransferStalledAsync(_state.InboundFileTransferId);

            var notifyClientError =
                $"{Environment.NewLine}Error occurred when notifying client that file transfer data " +
                $"is no longer being received:\n{notifyClientResult.Error}";

            return notifyClientResult.Success
                ? Result.Ok()
                : Result.Fail(notifyClientError);
        }
        
        async Task NotifiedRemoteServerThatFileTransferIsStalledAsync()
        {
            var getFileTransferResult = _state.LocalServer.GetFileTransferById(_inboundFileTransferId);
            if (getFileTransferResult.Success)
            {
                var inboundFileTransfer = getFileTransferResult.Value;
                FileHelper.DeleteFileIfAlreadyExists(inboundFileTransfer.LocalFilePath, 3);
            }

            var fileStalledTimeSpan = _state.FileStalledInfo.Elapsed.ToFormattedString();

            var sentFileStalledNotification =
                $"{Environment.NewLine}Successfully notified remote server that file transfer has " +
                $"stalled, {fileStalledTimeSpan} elapsed since last data received.";

            Console.WriteLine(sentFileStalledNotification);
            _state.SignalReturnToMainMenu.Set();

            await Task.Delay(Constants.OneHalfSecondInMilliseconds);
            _state.SignalReturnToMainMenu.WaitOne();
        }

        async Task HandleRetryLimitExceededAsync(ServerEvent serverEvent)
        {
            var fileName = serverEvent.FileName;
            var retryLimit = serverEvent.RemoteServerRetryLimit;
            var lockoutExpireTime = serverEvent.RetryLockoutExpireTime;

            var retryLImitExceeded =
                $"{Environment.NewLine}Maximum # of attempts to complete stalled file transfer reached or exceeded: " +
                $"{retryLimit} failed attempts for \"{fileName}\"{Environment.NewLine}" +
                $"You will be locked out from attempting this file transfer until {lockoutExpireTime:g}";

            Console.WriteLine(retryLImitExceeded);
            _state.SignalReturnToMainMenu.Set();
            
            await Task.Delay(Constants.OneHalfSecondInMilliseconds);
            _mainMenu.DisplayMenu();
        }
    }
}
