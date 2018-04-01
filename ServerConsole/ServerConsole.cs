namespace ServerConsole
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    using Commands.CompositeCommands;
    using Commands.Menus;

    using TplSockets;

    public class ServerConsole
    {
        const string SettingsFileName = "settings.xml";
        const string FileAlreadyExists = "\nThe client rejected the file transfer since a file by the same name already exists";
        const string FileTransferCancelled = "\nCancelled file transfer, client stopped receiving data and file transfer is incomplete.";

        const int SendMessage = 1;
        const int SendFile = 2;
        const int GetFile = 3;
        const int ShutDown = 4;
        
        const int ReplyToTextMessage = 1;
        const int EndTextSession = 2;
        
        readonly Logger _log = new Logger(typeof(ServerConsole));

        string _settingsFilePath;
        CancellationTokenSource _cts;
        AppState _state;

        public ServerConsole()
        {
            _log.Info("Begin: Instantiate ServerConsole");

            _settingsFilePath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SettingsFileName}";

            _cts = new CancellationTokenSource();

            _state = new AppState();

            _log.Info("Complete: Instantiate ServerConsole");
        }

        public event EventHandler<ServerEvent> EventOccurred;

        public async Task<Result> RunServerAsync()
        {
            _log.Info("Begin: ServerConsole.RunServerAsync");

            var initializeServerCommand = new InitializeServerCommand(_state, _settingsFilePath);
            var initializeServerResult = await initializeServerCommand.ExecuteAsync();

            if (initializeServerResult.Failure)
            {
                return Result.Fail(initializeServerResult.Error);
            }

            _state.Server.EventOccurred += HandleServerEvent;
            var token = _cts.Token;

            var result = Result.Fail(string.Empty);
            
            try
            {
                var listenTask =
                    Task.Run(() =>
                        _state.Server.HandleIncomingConnectionsAsync(),
                        token);

                while (_state.WaitingForServerToBeginAcceptingConnections) { }
                _state.Server.EventOccurred -= HandleServerEvent;

                var mainMenu = new MainMenu(_state);
                result = await mainMenu.ExecuteAsync();

                if (_state.ProgressBarInstantiated)
                {
                    _state.Progress.Dispose();
                    _state.ProgressBarInstantiated = false;
                }
                
                await _state.Server.ShutdownServerAsync();
                var shutdownServerResult = await listenTask;
                if (shutdownServerResult.Failure)
                {
                    Console.WriteLine(shutdownServerResult.Error);
                }                
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

            _log.Info("Complete: ServerConsole.RunServerAsync");
            return result;
        }

        void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            switch (serverEvent.EventType)
            {
                case EventType.ServerIsListening:
                    _state.WaitingForServerToBeginAcceptingConnections = false;
                    return;
            }
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
            
            _state.WaitingForConfirmationMessage = true;
            _state.FileTransferRejected = false;
            _state.FileTransferCanceled = false;

            var sendFileResult =
                await _state.Server.SendFileAsync(
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
                await _state.Server.GetFileAsync(
                    remoteIp,
                    remotePort,
                    fileToGet,
                    _state.Settings.TransferFolderPath).ConfigureAwait(false);

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
    }
}
