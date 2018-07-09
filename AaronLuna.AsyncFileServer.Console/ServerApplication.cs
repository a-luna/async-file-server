//TODO: Bring back functionality where when ServerInfo is requested, the server receiving the request checks if the requesting server exists in its list of remote servers. If it is not known, prompt user yes/no if they would like to add it, and if yes, ServerInfoRequest is sent. 
//TODO: Update AsyncFileServer.ToString() to be more useful when debugging. New format should incorporate the Name and OperatingSystem properties

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
    using Common.Network;
    using Common.Result;
    using ConsoleProgressBar;

    public class ServerApplication
    {
        const string SettingsFileName = "settings.xml";
        readonly int _displayMessageDelay = Constants.TwoSecondsInMilliseconds;

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
            _state = new AppState {MessageDisplayTime = _displayMessageDelay};
            _state.LocalServer.EventOccurred += HandleServerEventAsync;
            _state.LocalServer.SocketEventOccurred += HandleServerEventAsync;
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
                var userInterface = await _mainMenu.ExecuteAsync();
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
            else
            {
                var getCidrIp = NetworkUtilities.AttemptToDetermineLanCidrIp();
                if (getCidrIp.Success)
                {
                    var newCidrIp = getCidrIp.Value;
                    var cidrIpMatch = _state.Settings.LocalNetworkCidrIp == newCidrIp;
                    if (!cidrIpMatch)
                    {
                        var prompt = 
                            "The current value for CIDR IP is" +
                            $"{_state.Settings.LocalNetworkCidrIp}, however it appears " +
                            $"that {newCidrIp} is the correct value for the current LAN, " +
                            "would you like to use this value?";

                        var updateCidrIp = SharedFunctions.PromptUserYesOrNo(prompt);
                        if (updateCidrIp)
                        {
                            _state.Settings.LocalNetworkCidrIp = newCidrIp;
                            settingsChanged = true;
                        }
                    }
                }
            }

            await
                _state.LocalServer.InitializeAsync(
                    _state.Settings.LocalNetworkCidrIp,
                    _state.Settings.LocalServerPortNumber);

            _state.LocalServer.SocketSettings = _state.Settings.SocketSettings;
            _state.LocalServer.TransferUpdateInterval = _state.Settings.TransferUpdateInterval;
            _state.LocalServer.TransferRetryLimit = _state.Settings.TransferRetryLimit;
            _state.LocalServer.RetryLimitLockout = _state.Settings.RetryLimitLockout;
            _state.LocalServer.Info.TransferFolder = _state.Settings.LocalServerFolderPath;

            return settingsChanged
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
            _log.Error("Exception cought:", ex);
            Console.WriteLine(Environment.NewLine + ex.GetReport());
        }

        async void HandleServerEventAsync(object sender, ServerEvent serverEvent)
        {
            _log.Info(serverEvent.ToString());

            switch (serverEvent.EventType)
            {
                case ServerEventType.ProcessRequestStarted:
                    LookupRemoteServerName();
                    RefreshMainMenu(serverEvent);
                    break;

                case ServerEventType.ReceiveRequestFromRemoteServerComplete:                
                case ServerEventType.ProcessRequestComplete:
                case ServerEventType.ReceivedTextMessage:
                case ServerEventType.QueueContainsUnhandledRequests:
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
                    _state.FileTransferInProgress = true;
                    _mainMenu.DisplayMenu();
                    break;

                case ServerEventType.FileTransferStalled:
                case ServerEventType.SendFileBytesComplete:
                    _state.FileTransferInProgress = false;
                    _mainMenu.DisplayMenu();
                    break;

                case ServerEventType.ReceivedInboundFileTransferRequest:
                    ReceivedInboundFileTransferRequest(serverEvent);
                    break;

                case ServerEventType.ReceiveFileBytesStarted:
                    ReceiveFileBytesStarted(serverEvent);
                    break;

                case ServerEventType.ReceiveFileBytesComplete:
                    await ReceiveFileBytesCompleteAsync(serverEvent);
                    break;

                case ServerEventType.SendFileTransferRejectedStarted:
                    await RejectFileTransferAsync();
                    break;

                case ServerEventType.SendFileTransferStalledComplete:
                    await NotifiedRemoteServerThatFileTransferIsStalledAsync();
                    break;

                case ServerEventType.ReceivedRetryLimitExceeded:
                    await HandleRetryLimitExceededAsync(serverEvent);
                    break;
                    
                case ServerEventType.ErrorOccurred:
                    HandleErrorOccurred(serverEvent);
                    break;

                default:
                    return;
            }
        }

        private void LookupRemoteServerName()
        {
            if (!string.IsNullOrEmpty(_state.LocalServer.RemoteServerInfo.Name)) return;

            var foundName =
                SharedFunctions.LookupRemoteServerName(
                    _state.LocalServer.RemoteServerInfo,
                    _state.Settings.RemoteServers);

            if (foundName.Failure) return;

            _state.LocalServer.RemoteServerInfo.Name = foundName.Value;
        }

        void RefreshMainMenu(ServerEvent serverEvent)
        {
            if (_state.FileTransferInProgress) return;
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
            _state.SelectedServerInfo.TransferFolder = serverEvent.RemoteFolder;
            _state.SelectedServerInfo.PublicIpAddress = serverEvent.PublicIpAddress;
            _state.SelectedServerInfo.LocalIpAddress = serverEvent.LocalIpAddress;
            _state.SelectedServerInfo.SessionIpAddress = serverEvent.RemoteServerIpAddress;
            _state.SelectedServerInfo.Platform = serverEvent.RemoteServerPlatform;
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
                $"File Name..: {fileName}{Environment.NewLine}" +
                $"File Size..: {fileSize}{Environment.NewLine}" +
                $"Save To....: {localFolder}{Environment.NewLine}";
            
            Console.WriteLine(remoteServerInfo);
            Console.WriteLine(fileInfo);
        }

        void ReceiveFileBytesStarted(ServerEvent serverEvent)
        {
            var fileSize = serverEvent.FileSizeInBytes;
            var transferTimeout = _state.Settings.FileTransferStalledTimeout;

            _state.ProgressBar =
                new FileTransferProgressBar(fileSize, transferTimeout)
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

            await Task.Delay(Constants.OneHalfSecondInMilliseconds);
            _state.SignalReturnToMainMenu.WaitOne();
        }

        async void HandleStalledFileTransferAsync(object sender, ProgressEventArgs eventArgs)
        {
            var getFileTransferResult =
                _state.LocalServer.GetFileTransferById(_state.InboundFileTransferId);

            if (getFileTransferResult.Failure)
            {
                var error = 
                    $"{getFileTransferResult.Error} (ServerApplication.HandleStalledFileTransferAsync)";

                Console.WriteLine(error);
                return;
            }

            var inboundFileTransfer = getFileTransferResult.Value;

            _state.FileStalledInfo = eventArgs;
            _state.ProgressBar.Dispose();
            _state.ProgressBarInstantiated = false;
            
            var notifyStalledResult =
                await SendFileTransferStalledNotification(inboundFileTransfer.FiletransferId);

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
                await _state.LocalServer.SendNotificationFileTransferStalledAsync(fileTransferId);

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
                    inboundFileTransfer.FileTransfer.LocalFilePath,
                    3);
            }

            var fileStalledTimeSpan = _state.FileStalledInfo.Elapsed.ToFormattedString();

            var sentFileStalledNotification =
                $"{Environment.NewLine}Successfully notified remote server that file transfer " +
                $"has stalled, {fileStalledTimeSpan} elapsed since last data received.";

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
                $"{Environment.NewLine}Maximum # of attempts to complete stalled file transfer " +
                $"reached or exceeded: {retryLimit} failed attempts for \"{fileName}\"" +
                $"{Environment.NewLine}You will be locked out from attempting this " +
                $"file transfer until {lockoutExpireTime:g}";

            Console.WriteLine(retryLImitExceeded);
            _state.SignalReturnToMainMenu.Set();
            
            await Task.Delay(Constants.OneHalfSecondInMilliseconds);
            _mainMenu.DisplayMenu();
        }

        void HandleErrorOccurred(ServerEvent serverEvent)
        {
            _state.ErrorOccurred = true;
            _state.ErrorMessage = serverEvent.ErrorMessage;
            _state.FileTransferInProgress = false;

            if (!_state.ProgressBarInstantiated) return;

            _state.ProgressBar.Dispose();
            _state.ProgressBarInstantiated = false;
        }
    }
}
