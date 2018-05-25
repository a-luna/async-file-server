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

            return runServerResult;
        }

        public async Task<Result> InitializeServerAsync()
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
                case EventType.ServerStartedListening:
                    _state.WaitingForServerToBeginAcceptingConnections = false;
                    break;

                case EventType.ReceiveMessageFromClientComplete:

                    if (serverEvent.MessageType == MessageType.ShutdownServerCommand) return;

                    if (!serverEvent.Message.MustBeProcessedImmediately())
                    {
                        Console.WriteLine($"{Environment.NewLine}New {serverEvent.Message.Type.Name()} from {serverEvent.RemoteServerIpAddress}, added to queue");
                        await Task.Delay(Constants.TwoSecondsInMilliseconds);
                        _mainMenu.DisplayMenu();
                    }

                    break;

                case EventType.ProcessRequestComplete:

                    if (serverEvent.MessageType == MessageType.ShutdownServerCommand) return;

                    if (serverEvent.MessageType.MustBeProcessedImmediately())
                    {
                        Console.WriteLine($"{Environment.NewLine}Processed {serverEvent.MessageType.Name()} from {serverEvent.RemoteServerIpAddress}, addded to archive");
                        await Task.Delay(Constants.TwoSecondsInMilliseconds);
                        _mainMenu.DisplayMenu();
                    }

                    break;

                case EventType.ReceivedTextMessage:

                    Console.WriteLine($"\n{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} says:");
                    Console.WriteLine(serverEvent.TextMessage);

                    Console.WriteLine($"{Environment.NewLine}Press enter to return to the main menu.");
                    Console.ReadLine();

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

                case EventType.ReceivedInboundFileTransferRequest:
                    Console.WriteLine($"\nIncoming file transfer from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}:");
                    Console.WriteLine($"File Name:\t{serverEvent.FileName}\nFile Size:\t{serverEvent.FileSizeString}\nSave To:\t{serverEvent.LocalFolder}");
                    break;

                case EventType.ReceiveFileBytesStarted:
                    ReceiveFileBytesStarted(serverEvent);
                    break;

                case EventType.ReceiveFileBytesComplete:
                    await ReceiveFileBytesCompleteAsync(serverEvent);
                    break;

                case EventType.SendFileTransferRejectedStarted:
                    Console.WriteLine("\nA file with the same name already exists in the download folder, please rename or remove this file in order to proceed");
                    break;

                case EventType.SendFileTransferRejectedComplete:
                    await Task.Delay(Constants.TwoSecondsInMilliseconds);
                    _mainMenu.DisplayMenu();
                    break;

                case EventType.SendFileTransferStalledComplete:
                    FileTransferStalled();
                    break;

                case EventType.SendShutdownServerCommandStarted:
                    _state.ShutdownInitiated = true;
                    break;

                case EventType.ErrorOccurred:
                    _state.ErrorOccurred = true;
                    break;

                default:
                    return;
            }
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

        void HandleFileTransferProgress(object sender, ServerEvent serverEvent)
        {
            _state.ProgressBar.BytesReceived = serverEvent.TotalFileBytesReceived;
            _state.ProgressBar.Report(serverEvent.PercentComplete);
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
            await Task.Delay(Constants.OneHalfSecondInMilliseconds);

            _state.ProgressBar.Dispose();
            _state.ProgressBarInstantiated = false;

            _state.RetryCounter = 0;
            _state.SignalRetryLimitExceeded.Set();
        }

        async void HandleStalledFileTransferAsync(object sender, ProgressEventArgs eventArgs)
        {
            _state.FileStalledInfo = eventArgs;
            _state.FileTransferStalled = true;

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
                    "Maximum # of attempts to complete stalled file transfer reached or exceeded: " +
                    $"({_state.Settings.MaxDownloadAttempts} failed attempts for \"{_state.IncomingFileName}\")";

                Console.WriteLine(maxRetriesReached);

                var folder = _state.Settings.LocalServerFolderPath;
                var filePath = Path.Combine(folder, _state.IncomingFileName);

                FileHelper.DeleteFileIfAlreadyExists(filePath);

                _state.SignalRetryLimitExceeded.Set();
                return;
            }

            var userPrompt = $"Try again to download file \"{_state.IncomingFileName}\" from {_state.SelectedServer.SessionIpAddress}:{_state.SelectedServer.Port}?";
            if (SharedFunctions.PromptUserYesOrNo(userPrompt))
            {
                _state.RetryCounter++;
                await _state.LocalServer.RetryLastFileTransferAsync(
                    _state.SelectedServer.SessionIpAddress,
                    _state.SelectedServer.Port);
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
