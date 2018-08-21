//TODO: Determine if it will be simple or difficult to show deconstructed request bytes and how each region maps to fileNameLen, fileName, portNumLen, portNum, etc
//TODO: On event log menus, enable filter functionality to allow user to view only desired request types, transfer types, events/requests/transfers for desired servers, request/transfer status types, etc.
//TODO: Create a new class Model.ServerFolder with properties ID (int) and Name (string), and private field _folderPath. Replace TransferFolder property of ServerInfo with DownloadFolders (List<ServerFolder>). When user request ServerInfo, the response includes a list of folder IDs and Names. When RequestFileList, the ID is sent. This avoids the bugs caused by the Path class behaving differently on Windows vs Unix systems. When calling GetFile(), fileName, folderId are sent to remote server. ServerRequest.FolderDoesNotExist will be changed to InvalidFolderID, etc.
//TODO: Add property ReceivedFilesFolder (ServerFolder) to ServerSettings, use this for all inbound file transfers. Should be unique from DownloadFolders but this will not be enforced in the code.
//TODO: Change AsyncFileServer API to use ServerInfo objects and folderId (int) instead of ipaddress, portNumber, folderPath, etc. Only public method that accepts IP,Port should be RequestServerInfo()
//TODO: Create IsEqualTo() method for FileTransferController. Check when outbound transfer is requested to maintain the integrity of the retry limit/lockout behavior
//TODO: If user calls GetFile() multiple times for the same file/remote server, display an error indicating that this transfer is already pending, do not send request. Also, check if it aleady exists in target folder and do not send request.

using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.CLI.Menus;
using AaronLuna.AsyncSocketServer.Requests;
using AaronLuna.Common;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.IO;
using AaronLuna.Common.Logging;
using AaronLuna.Common.Result;
using AaronLuna.ConsoleProgressBar;

namespace AaronLuna.AsyncSocketServer.CLI
{
    public class ServerApplication
    {
        const string XmlSettingsFileName = "settings.xml";

        readonly Logger _log = new Logger(typeof(ServerApplication));
        readonly string _xmlSettingsFilePath;
        readonly CancellationTokenSource _cts;
        AppState _state;
        MainMenu _mainMenu;

        public ServerApplication()
        {
            _xmlSettingsFilePath =
                $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{XmlSettingsFileName}";

            _cts = new CancellationTokenSource();
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

                while (!_state.LocalServer.IsRunning) { }

                _mainMenu = new MainMenu(_state);
                var userInterface = await _mainMenu.ExecuteAsync().ConfigureAwait(false);
                if (userInterface.Failure)
                {
                    SharedFunctions.ModalMessage(userInterface.Error, Resources.Prompt_PressEnterToContinue);
                }

                if (runServerTask == await
                        Task.WhenAny(
                            runServerTask,
                            Task.Delay(Constants.ThreeSecondsInMilliseconds, token))
                            .ConfigureAwait(false))
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
            var getSettings = GetServerSettings(_xmlSettingsFilePath);
            if (getSettings.Failure)
            {
                var error =
                    "Unable to write to settings.xml file, must abort server program. For more information " +
                    "please see the error message below:" +
                    Environment.NewLine + Environment.NewLine + getSettings.Error;

                return Result.Fail(error);
            }

            _state = new AppState(getSettings.Value);
            _state.LocalServer.EventOccurred += HandleServerEventAsync;
            _state.LocalServer.SocketEventOccurred += HandleServerEventAsync;
            _state.LocalServer.FileTransferProgress += HandleFileTransferProgress;

            _state.SettingsFile = new FileInfo(_xmlSettingsFilePath);
            _state.UserEntryPortNumber = _state.Settings.LocalServerPortNumber;
            _state.UserEntryLocalNetworkCidrIp = _state.Settings.LocalNetworkCidrIp;

            CheckLocalServerSettings();

            await _state.LocalServer.InitializeAsync().ConfigureAwait(false);
            
            return Result.Ok();
        }

        static Result<ServerSettings> GetServerSettings(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return GetDefaultSettings(filePath);
            }

            var getSettingsFromFile = ServerSettings.ReadFromFile(filePath);
            if (getSettingsFromFile.Success) return Result.Ok(getSettingsFromFile.Value);

            var getDefaultSettings = HandleXmlDeserializationError(filePath, getSettingsFromFile.Error);

            return getDefaultSettings.Success
                ? Result.Ok(getDefaultSettings.Value)
                : getDefaultSettings;
        }

        static Result<ServerSettings> GetDefaultSettings(string filePath)
        {
            var defaultSettings = ServerSettings.GetDefaultSettings();
            var saveSettings = ServerSettings.SaveToFile(defaultSettings, filePath);

            return saveSettings.Success
                ? Result.Ok(defaultSettings)
                : Result.Fail<ServerSettings>(saveSettings.Error);
        }

        static Result<ServerSettings> HandleXmlDeserializationError(string filePath, string xmlError)
        {
            var settingsFileName = Path.GetFileName(filePath);
            var folderPath = Path.GetDirectoryName(filePath);

            var error =
                $"Unable to deserialize the settings XML file \"{settingsFileName}\" " +
                $"located in folder:{Environment.NewLine}{Environment.NewLine}" +
                folderPath + Environment.NewLine + Environment.NewLine +
                $"The following error was encountered:{Environment.NewLine}{xmlError}";

            Console.Clear();
            SharedFunctions.ModalMessage(error, Resources.Prompt_PressEnterToContinue);

            var badXmlFileName = $"settings_backup_{Logging.GetTimeStampForFileName()}.xml";
            var backupFilePath = Path.Combine(folderPath, badXmlFileName);
            File.Move(filePath, backupFilePath);

            var getDefaultSettings = GetDefaultSettings(filePath);
            if (getDefaultSettings.Failure)
            {
                return getDefaultSettings;
            }

            var message =
                $"Created new settings xml file \"{settingsFileName}\" with default values.{Environment.NewLine}" +
                $"The XML file that could not be deserialized has been saved as \"{badXmlFileName}\"." +
                $"{Environment.NewLine}{Environment.NewLine}You can change the default settings " +
                $"at any time from the \"{Resources.MenuItemText_LocalServerSettings}\" menu item";

            Console.Clear();
            SharedFunctions.ModalMessage(message, Resources.Prompt_PressEnterToContinue);

            return Result.Ok(getDefaultSettings.Value);
        }

        void CheckLocalServerSettings()
        {
            var portNumberHasChanged = false;
            bool cidrIpHasChanged;

            if (_state.Settings.LocalServerPortNumber == 0)
            {
                _state.Settings.LocalServerPortNumber =
                    SharedFunctions.GetPortNumberFromUser(
                        _state,
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

            if (portNumberHasChanged || cidrIpHasChanged)
            {
                _state.SaveSettingsToFile();
            }
        }

        async void HandleServerEventAsync(object sender, ServerEvent serverEvent)
        {
            _log.Info(serverEvent.GetLogFileEntry());

            switch (serverEvent.EventType)
            {
                case EventType.ProcessRequestComplete:
                    await CheckIfRemoteServerAlreadySavedAsync(serverEvent);
                    RefreshMainMenu(serverEvent);
                    break;

                case EventType.ReceivedTextMessage:
                case EventType.PendingFileTransfer:
                case EventType.SendFileTransferRejectedStarted:
                    RefreshMainMenu(serverEvent);
                    break;

                case EventType.ReceivedServerInfo:
                    ReceivedServerInfo(serverEvent);
                    break;

                case EventType.ReceivedFileList:
                    _state.RemoteServerFileList = serverEvent.RemoteServerFileList;
                    _state.WaitingForFileListResponse = false;
                    return;

                case EventType.ReceivedNotificationFolderIsEmpty:
                    _state.NoFilesAvailableForDownload = true;
                    _state.WaitingForFileListResponse = false;
                    break;

                case EventType.ReceivedNotificationFolderDoesNotExist:
                    _state.RequestedFolderDoesNotExist = true;
                    _state.WaitingForFileListResponse = false;
                    break;

                case EventType.SendFileBytesStarted:
                    _state.OutboundFileTransferInProgress = true;
                    _mainMenu.DisplayMenu();
                    break;

                case EventType.FileTransferStalled:
                case EventType.SendFileBytesComplete:
                    _state.OutboundFileTransferInProgress = false;
                    _mainMenu.DisplayMenu();
                    break;

                case EventType.ReceiveFileBytesStarted:
                    _state.InboundFileTransferInProgress = true;
                    ReceiveFileBytesStarted(serverEvent);
                    break;

                case EventType.ReceiveFileBytesComplete:
                    _state.InboundFileTransferInProgress = false;
                    await ReceiveFileBytesCompleteAsync(serverEvent).ConfigureAwait(false);
                    break;

                case EventType.SendFileTransferStalledComplete:
                    await NotifiedRemoteServerThatFileTransferIsStalledAsync().ConfigureAwait(false);
                    break;

                case EventType.ErrorOccurred:
                    HandleErrorOccurred();
                    _mainMenu.DisplayMenu();
                    break;

                default:
                    return;
            }
        }

        void HandleFileTransferProgress(object sender, ServerEvent serverEvent)
        {
            if (_state.OutboundFileTransferInProgress) return;

            _state.InboundFileTransferId = serverEvent.FileTransferId;
            _state.ProgressBar.BytesReceived = serverEvent.TotalFileBytesReceived;
            _state.ProgressBar.Report(serverEvent.PercentComplete);
        }

        void HandleException(Exception ex)
        {
            _log.Error("Exception caught:", ex);
            Console.WriteLine(ex.GetReport());
        }

        private async Task CheckIfRemoteServerAlreadySavedAsync(ServerEvent serverEvent)
        {
            if (_state.DoNotRequestServerInfo) return;

            var exists = SharedFunctions.CheckIfRemoteServerAlreadySaved(
                _state.LocalServer.MyInfo,
                serverEvent.RemoteServerIpAddress,
                serverEvent.RemoteServerPortNumber,
                _state.Settings.RemoteServers);

            if (exists) return;

            _state.DoNotRequestServerInfo = true;
            _state.DoNotRefreshMainMenu = true;
            _state.PromptUserForServerName = false;

            await
                SharedFunctions.RequestServerInfoAsync(
                    _state,
                    serverEvent.RemoteServerIpAddress,
                    serverEvent.RemoteServerPortNumber);

            _state.DoNotRequestServerInfo = false;
            _state.DoNotRefreshMainMenu = false;
        }

        void RefreshMainMenu(ServerEvent serverEvent)
        {
            if (_state.OutboundFileTransferInProgress) return;
            if (DoNotRefreshMainMenu(serverEvent.RequestType)) return;

            Thread.Sleep(Constants.OneHalfSecondInMilliseconds);
            _mainMenu.DisplayMenu();
        }

        bool DoNotRefreshMainMenu(RequestType messageType)
        {
            switch (messageType)
            {
                case RequestType.MessageRequest:
                case RequestType.RequestedFolderDoesNotExist:
                case RequestType.RequestedFolderIsEmpty:
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

        void ReceivedServerInfo(ServerEvent serverEvent)
        {
            var serverInfo = new ServerInfo
            {
                TransferFolder = serverEvent.RemoteFolder,
                PublicIpAddress = serverEvent.PublicIpAddress,
                LocalIpAddress = serverEvent.LocalIpAddress,
                SessionIpAddress = serverEvent.RemoteServerIpAddress,
                PortNumber = serverEvent.RemoteServerPortNumber,
                Platform = serverEvent.RemoteServerPlatform
            };

            if (_state.PromptUserForServerName)
            {
                _state.SelectedServerInfo = serverInfo;
                SharedFunctions.DisplayLocalServerInfo(_state);

                _state.SelectedServerInfo.Name =
                    SharedFunctions.SetSelectedServerName(_state, _state.SelectedServerInfo);

                _state.WaitingForUserToConfirmServerDetails = false;
            }

            _state.Settings.RemoteServers.Add(serverInfo);
            _state.SaveSettingsToFile();
            _state.WaitingForServerInfoResponse = false;
        }

        void ReceiveFileBytesStarted(ServerEvent serverEvent)
        {
            _state.InboundFileTransferId = serverEvent.FileTransferId;
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

            var getFileTransfer = _state.LocalServer.GetFileTransferById(_state.InboundFileTransferId);
            if (getFileTransfer.Failure)
            {
                SharedFunctions.ModalMessage(
                    getFileTransfer.Error,
                    Resources.Prompt_PressEnterToContinue);

                return;
            }

            var inboundFileTransfer = getFileTransfer.Value;

            SharedFunctions.DisplayLocalServerInfo(_state);
            Console.WriteLine(inboundFileTransfer.InboundRequestDetails(true));
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

            TearDownProgressBar();
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
            TearDownProgressBar();

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
                _state.LocalServer.GetFileTransferById(_state.InboundFileTransferId);

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

        void HandleErrorOccurred()
        {
            if (!_state.OutboundFileTransferInProgress) return;

            TearDownProgressBar();
            _state.OutboundFileTransferInProgress = false;
        }

        void TearDownProgressBar()
        {
            if (!_state.ProgressBarInstantiated) return;

            _state.ProgressBar.Dispose();
            _state.ProgressBarInstantiated = false;
        }
    }
}
