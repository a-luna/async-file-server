//TODO: Figure out best eay to prompt user to provide a name for a server that was added passively following any process request complete event
//TODO: Update AsyncFileServer.ToString() to be more useful when debugging. New format should possibly incorporate: Name, Local IP, Port #, request/transfer counts?
//TODO: Determine if it will be simple or difficult to show deconstructed request bytes and how each region maps to fileNameLen, fileName, portNumLen, portNum, etc
//TODO: To avoid crashes, when there is an error reading settings from xml, handle exception, create default settings object and proceed. Rename the offending xml file and write new settings.xml to file.
//TODO: In the AaronLuna.AsyncFileServer.Test namespace, create a filetransfercontroller subclass that overrides the defaut SendFileAsync() behavior to send only 10% of a file in order to be used within a set of unit tests that verify the stalled timeout/retry counter/limit/lockout timespan settings are working correctly.

namespace AaronLuna.AsyncFileServer.Console
{
    using System;
    using System.IO;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    using Menus;
    using Model;
    using Common;
    using Common.Extensions;
    using Common.IO;
    using Common.Logging;
    using Common.Result;
    using ConsoleProgressBar;

    public class ServerApplication
    {
        const string SettingsFileName = "settings.xml";
        readonly int _messageDisplayTime = Constants.TwoSecondsInMilliseconds;

        readonly Logger _log = new Logger(typeof(ServerApplication));
        readonly string _settingsFilePath;
        readonly CancellationTokenSource _cts;
        readonly AppState _state;
        MainMenu _mainMenu;
        int _inboundFileTransferId;

        public ServerApplication()
        {
            _settingsFilePath =
                $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SettingsFileName}";

            _cts = new CancellationTokenSource();
            _state = new AppState {MessageDisplayTime = _messageDisplayTime};
            _state.LocalServer.EventOccurred += HandleServerEventAsync;
            _state.LocalServer.SocketEventOccurred += HandleServerEventAsync;
            _state.LocalServer.FileTransferProgress += HandleFileTransferProgress;
        }

        public async Task<Result> RunAsync()
        {
            var initializeServerResult = await InitializeServerAsync().ConfigureAwait(false);
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
                var userInterface = await _mainMenu.ExecuteAsync().ConfigureAwait(false);
                if (userInterface.Failure)
                {
                    Console.WriteLine($"Error: {userInterface.Error}");
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
                HandleException(ex);
            }
            catch (SocketException ex)
            {
                HandleException(ex);
            }
            catch (TaskCanceledException ex)
            {
                HandleException(ex);
            }
            catch (IOException ex)
            {
                HandleException(ex);
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            return runServerResult;
        }

        async Task<Result> InitializeServerAsync()
        {
            InitializeSettings();
            var portNumberHasChanged = false;
            bool cidrIpHasChanged;

            if (_state.Settings.LocalServerPortNumber == 0)
            {
                _state.Settings.LocalServerPortNumber =
                    SharedFunctions.GetPortNumberFromUser(
                        Resources.Prompt_SetLocalPortNumber,
                        true);

                portNumberHasChanged = true;
            }

            if (string.IsNullOrEmpty(_state.Settings.LocalNetworkCidrIp))
            {
                _state.Settings.LocalNetworkCidrIp = SharedFunctions.InitializeLanCidrIp(_state);
                cidrIpHasChanged = true;
            }
            else
            {
                cidrIpHasChanged = SharedFunctions.CidrIpHasChanged(_state);
            }

            await
                _state.LocalServer.InitializeAsync(
                    _state.Settings.LocalNetworkCidrIp,
                    _state.Settings.LocalServerPortNumber).ConfigureAwait(false);

            _state.LocalServer.Settings.SocketSettings = _state.Settings.SocketSettings;
            _state.LocalServer.Settings.TransferUpdateInterval = _state.Settings.TransferUpdateInterval;
            _state.LocalServer.Settings.TransferRetryLimit = _state.Settings.TransferRetryLimit;
            _state.LocalServer.Settings.RetryLimitLockout = _state.Settings.RetryLimitLockout;
            _state.LocalServer.MyInfo.TransferFolder = _state.Settings.LocalServerFolderPath;

            var anySettingWasChanged = portNumberHasChanged || cidrIpHasChanged;

            return anySettingWasChanged
                ? ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath)
                : Result.Ok();
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
            _state.UserEntryPortNumber = _state.Settings.LocalServerPortNumber;
            _state.UserEntryLocalNetworkCidrIp = _state.Settings.LocalNetworkCidrIp;
        }

        void HandleException(Exception ex)
        {
            _log.Error("Exception caught:", ex);
            Console.WriteLine(Environment.NewLine + ex.GetReport());
        }

        async void HandleServerEventAsync(object sender, ServerEvent serverEvent)
        {
            _log.Info(serverEvent.GetLogFileEntry());

            switch (serverEvent.EventType)
            {
                case ServerEventType.ProcessRequestComplete:
                    await SharedFunctions.CheckIfRemoteServerAlreadySavedAsync(_state);
                    RefreshMainMenu(serverEvent);
                    break;

                case ServerEventType.ReceiveRequestFromRemoteServerComplete:
                case ServerEventType.ReceivedTextMessage:
                case ServerEventType.QueueContainsUnhandledRequests:
                case ServerEventType.SendFileTransferRejectedStarted:
                    RefreshMainMenu(serverEvent);
                    break;

                case ServerEventType.ReceivedServerInfo:
                    ReceivedServerInfo(serverEvent);
                    _state.WaitingForServerInfoResponse = false;
                    break;

                case ServerEventType.ReceivedFileList:
                    _state.WaitingForFileListResponse = false;
                    return;

                case ServerEventType.ReceivedNotificationNoFilesToDownload:
                    _state.NoFilesAvailableForDownload = true;
                    _state.WaitingForFileListResponse = false;
                    break;

                case ServerEventType.ReceivedNotificationFolderDoesNotExist:
                    _state.RequestedFolderDoesNotExist = true;
                    _state.WaitingForFileListResponse = false;
                    break;

                case ServerEventType.SendFileBytesStarted:
                    _state.OutboundFileTransferInProgress = true;
                    _mainMenu.DisplayMenu();
                    break;

                case ServerEventType.FileTransferStalled:
                case ServerEventType.SendFileBytesComplete:
                    _state.OutboundFileTransferInProgress = false;
                    _mainMenu.DisplayMenu();
                    break;

                case ServerEventType.ReceiveFileBytesStarted:
                    _state.InboundFileTransferInProgress = true;
                    ReceiveFileBytesStarted(serverEvent);
                    break;

                case ServerEventType.ReceiveFileBytesComplete:
                    _state.InboundFileTransferInProgress = false;
                    await ReceiveFileBytesCompleteAsync(serverEvent).ConfigureAwait(false);
                    break;

                case ServerEventType.SendFileTransferStalledComplete:
                    await NotifiedRemoteServerThatFileTransferIsStalledAsync().ConfigureAwait(false);
                    break;

                case ServerEventType.ReceivedRetryLimitExceeded:
                    await HandleRetryLimitExceededAsync(serverEvent).ConfigureAwait(false);
                    break;

                case ServerEventType.ErrorOccurred:
                    HandleErrorOccurred(serverEvent);
                    break;

                default:
                    return;
            }
        }

        void RefreshMainMenu(ServerEvent serverEvent)
        {
            if (_state.OutboundFileTransferInProgress) return;
            if (DoNotRefreshMainMenu(serverEvent.RequestType)) return;

            Thread.Sleep(Constants.OneHalfSecondInMilliseconds);
            _mainMenu.DisplayMenu();
        }

        bool DoNotRefreshMainMenu(ServerRequestType messageType)
        {
            switch (messageType)
            {
                case ServerRequestType.TextMessage:
                case ServerRequestType.RequestedFolderDoesNotExist:
                case ServerRequestType.NoFilesAvailableForDownload:
                case ServerRequestType.FileListRequest:
                case ServerRequestType.FileListResponse:
                case ServerRequestType.FileTransferAccepted:
                case ServerRequestType.InboundFileTransferRequest:
                case ServerRequestType.ShutdownServerCommand:
                    return true;

                default:
                    return false;
            }
        }
        
        void ReceivedServerInfo(ServerEvent serverEvent)
        {
            _state.SelectedServerInfo.TransferFolder = serverEvent.RemoteFolder;
            _state.SelectedServerInfo.PublicIpAddress = serverEvent.PublicIpAddress;
            _state.SelectedServerInfo.LocalIpAddress = serverEvent.LocalIpAddress;
            _state.SelectedServerInfo.SessionIpAddress = serverEvent.RemoteServerIpAddress;
            _state.SelectedServerInfo.Platform = serverEvent.RemoteServerPlatform;
        }

        void ReceiveFileBytesStarted(ServerEvent serverEvent)
        {
            _inboundFileTransferId = serverEvent.FileTransferId;
            var fileSize = serverEvent.FileSizeInBytes;
            var transferTimeout = _state.Settings.FileTransferStalledTimeout;

            _state.ProgressBar =
                new FileTransferProgressBar(fileSize, transferTimeout)
                {
                    NumberOfBlocks = 20,
                    StartBracket = string.Empty,
                    EndBracket = string.Empty,
                    CompletedBlock = "\u2022",
                    IncompleteBlock = "·",
                    AnimationSequence = ProgressAnimations.RotatingPipe
                };

            _state.ProgressBar.FileTransferStalled += HandleStalledFileTransferAsync;
            _state.ProgressBarInstantiated = true;
            
            var getFileTransfer = _state.LocalServer.GetFileTransferById(_inboundFileTransferId);
            if (getFileTransfer.Failure)
            {
                SharedFunctions.NotifyUserErrorOccurred(getFileTransfer.Error);
                return;
            }

            var inboundFileTransfer = getFileTransfer.Value;

            SharedFunctions.DisplayLocalServerInfo(_state);
            Console.WriteLine(inboundFileTransfer.InboundRequestDetails());
        }

        void HandleFileTransferProgress(object sender, ServerEvent serverEvent)
        {
            if (_state.OutboundFileTransferInProgress) return;

            _state.ProgressBar.BytesReceived = serverEvent.TotalFileBytesReceived;
            _state.ProgressBar.Report(serverEvent.PercentComplete);
        }

        async Task ReceiveFileBytesCompleteAsync(ServerEvent serverEvent)
        {
            var fileSize = serverEvent.FileSizeInBytes;
            var startTime = serverEvent.FileTransferStartTime;
            var completeTime = serverEvent.FileTransferCompleteTime;
            var timeElapsed = (completeTime - startTime).ToFormattedString();
            var transferRate = serverEvent.FileTransferRate;

            _state.ProgressBar.BytesReceived = fileSize;
            _state.ProgressBar.Report(1);
            Thread.Sleep(Constants.OneHalfSecondInMilliseconds);

            var report =
                Environment.NewLine + Environment.NewLine +
                $"Download Started...: {startTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                $"Download Finished..: {completeTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                $"Elapsed Time.......: {timeElapsed}{Environment.NewLine}" +
                $"Transfer Rate......: {transferRate}";

            Console.WriteLine(report);
            _state.SignalReturnToMainMenu.Set();

            _state.ProgressBar.Dispose();
            _state.ProgressBarInstantiated = false;

            await Task.Delay(Constants.OneHalfSecondInMilliseconds).ConfigureAwait(false);
            _state.SignalReturnToMainMenu.WaitOne();
        }

        async void HandleStalledFileTransferAsync(object sender, ProgressEventArgs eventArgs)
        {
            var getFileTransfer =
                _state.LocalServer.GetFileTransferById(_state.InboundFileTransferId);

            if (getFileTransfer.Failure)
            {
                var error =
                    $"{getFileTransfer.Error} (ServerApplication.HandleStalledFileTransferAsync)";

                Console.WriteLine(error);
                return;
            }

            var inboundFileTransfer = getFileTransfer.Value;

            _state.FileStalledInfo = eventArgs;
            _state.ProgressBar.Dispose();
            _state.ProgressBarInstantiated = false;

            var notifyStalledResult =
                await SendFileTransferStalledNotification(inboundFileTransfer.Id).ConfigureAwait(false);

            if (notifyStalledResult.Failure)
            {
                Console.WriteLine(notifyStalledResult.Error);
            }
        }

        async Task<Result> SendFileTransferStalledNotification(int fileTransferId)
        {
            var fileStalledMessage =
                $"{Environment.NewLine}{Environment.NewLine}Data is no longer being received " +
                "from remote server, attempting to cancel file transfer...";

            Console.WriteLine(fileStalledMessage);

            var notifyClientResult =
                await _state.LocalServer.SendNotificationFileTransferStalledAsync(fileTransferId).ConfigureAwait(false);

            var notifyClientError =
                $"{Environment.NewLine}Error occurred when notifying client that file transfer " +
                $"data is no longer being received:\n{notifyClientResult.Error}";

            return notifyClientResult.Success
                ? Result.Ok()
                : Result.Fail(notifyClientError);
        }

        async Task NotifiedRemoteServerThatFileTransferIsStalledAsync()
        {
            var getFileTransferResult =
                _state.LocalServer.GetFileTransferById(_inboundFileTransferId);

            if (getFileTransferResult.Success)
            {
                var inboundFileTransfer = getFileTransferResult.Value;

                FileHelper.DeleteFileIfAlreadyExists(
                    inboundFileTransfer.LocalFilePath,
                    3);
            }

            var fileStalledTimeSpan = _state.FileStalledInfo.Elapsed.ToFormattedString();

            var sentFileStalledNotification =
                $"{Environment.NewLine}Successfully notified remote server that file transfer " +
                $"has stalled, {fileStalledTimeSpan} elapsed since last data received.";

            Console.WriteLine(sentFileStalledNotification);
            _state.SignalReturnToMainMenu.Set();

            await Task.Delay(Constants.OneHalfSecondInMilliseconds).ConfigureAwait(false);
            _state.SignalReturnToMainMenu.WaitOne();
        }

        async Task HandleRetryLimitExceededAsync(ServerEvent serverEvent)
        {
            var fileName = serverEvent.FileName;
            var retryLimit = serverEvent.RemoteServerRetryLimit;
            var lockoutExpireTime = serverEvent.RetryLockoutExpireTime;

            var retryLImitExceeded =
                $"{Environment.NewLine}Maximum # of attempts to complete stalled file transfer " +
                $"reached or exceeded: {retryLimit} failed attempts for \"{fileName}\"" +
                $"{Environment.NewLine}You will be locked out from attempting this " +
                $"file transfer until {lockoutExpireTime:g}";

            Console.WriteLine(retryLImitExceeded);
            _state.SignalReturnToMainMenu.Set();

            await Task.Delay(Constants.OneHalfSecondInMilliseconds).ConfigureAwait(false);
            _mainMenu.DisplayMenu();
        }

        void HandleErrorOccurred(ServerEvent serverEvent)
        {
            TearDownProgressBar();

            _state.ErrorOccurred = true;
            _state.ErrorMessage = serverEvent.ErrorMessage;
            _state.OutboundFileTransferInProgress = false;

           SharedFunctions.NotifyUserErrorOccurred(serverEvent.ErrorMessage);
        }

        void TearDownProgressBar()
        {
            if (!_state.ProgressBarInstantiated) return;

            _state.ProgressBar.Dispose();
            _state.ProgressBarInstantiated = false;
        }
    }
}
