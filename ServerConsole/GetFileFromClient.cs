namespace ServerConsole
{
    using AaronLuna.Common.Console;
    using AaronLuna.Common.Numeric;
    using AaronLuna.Common.Result;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;    
    using TplSocketServer;

    public class GetFileFromClient
    {
        IProgress<double> _progress;
        bool _waitingForFileListResponse = true;
        bool _waitingForDownloadToComplete = true;
        List<(string filePath, long fileSize)> _fileInfoList;

        public async Task<Result> RunAsync(
            AppSettings settings, 
            ConnectionInfo listenServerInfo, 
            string clientIpAddress, 
            int clientPort)
        {
            _progress = new ConsoleProgressBar();

            var cts = new CancellationTokenSource();
            var token = cts.Token;

            var server = new TplSocketServer(settings);
            server.EventOccurred += HandleServerEvent;

            var randomPort = 0;
            while (randomPort is 0)
            {
                var random = new Random();
                randomPort = random.Next(Program.PortRangeMin, Program.PortRangeMax + 1);

                if (randomPort == listenServerInfo.Port)
                {
                    randomPort = 0;
                }
            }

            var listenTask =
                Task.Run(
                    () => server.HandleIncomingConnectionsAsync(
                        listenServerInfo.GetLocalIpAddress(),
                        randomPort,
                        token),
                    token);

            Console.WriteLine($"Requesting list of files from {clientIpAddress}:{clientPort}...");

            var requestFileListResult =
                await server.RequestFileListAsync(
                    clientIpAddress,
                    clientPort,
                    listenServerInfo.LocalIpAddress,
                    randomPort,
                    settings.TransferFolderPath,
                    token)
                    .ConfigureAwait(false);

            if (requestFileListResult.Failure)
            {
                return Result.Fail(
                    $"Error requesting list of available files from client:\n{requestFileListResult.Error}");
            }

            while (_waitingForFileListResponse) { }

            var fileDownloadResult =  await DownloadFileFromClient(
                _fileInfoList,
                clientIpAddress,
                clientPort,
                listenServerInfo.LocalIpAddress,
                randomPort,
                settings.TransferFolderPath)
                .ConfigureAwait(false);

            if (fileDownloadResult.Failure)
            {
                return fileDownloadResult;
            }

            while (_waitingForDownloadToComplete) { }

            try
            {
                cts.Cancel();
                var serverShutdown = await listenTask.ConfigureAwait(false);
                if (serverShutdown.Failure)
                {
                    Console.WriteLine($"There was an error shutting down the server: {serverShutdown.Error}");
                }
            }
            catch (AggregateException ex)
            {
                Console.WriteLine("\nException messages:");
                foreach (var ie in ex.InnerExceptions)
                {
                    Console.WriteLine($"\t{ie.GetType().Name}: {ie.Message}");
                }
            }
            finally
            {
                server.CloseListenSocket();
            }

            return Result.Ok();
        }

        private static async Task<Result> DownloadFileFromClient(
            List<(string filePath, long fileSizeInBytes)> fileInfoList,
            string remoteIp,
            int remotePort,
            string localIp,
            int localPort,
            string localFolder
        )
        {
            var selectFileResult = ChooseFileToGet(fileInfoList);
            if (selectFileResult.Failure)
            {
                Console.WriteLine(selectFileResult.Error);
                return Result.Fail(selectFileResult.Error);
            }

            var fileToGet = selectFileResult.Value;

            var settings = Program.InitializeAppSettings();
            var transferServer = new TplSocketServer(settings);

            var getFileResult =
                await transferServer.GetFileAsync(
                    remoteIp,
                    remotePort,
                    fileToGet,
                    localIp,
                    localPort,
                    localFolder,
                    new CancellationToken(false))
                    .ConfigureAwait(false);

            return getFileResult.Failure ? getFileResult : Result.Ok();
        }

        private static Result<string> ChooseFileToGet(List<(string filePath, long fileSizeInBytes)> fileInfoList)
        {
            var fileMenuChoice = 0;
            var totalMenuChoices = fileInfoList.Count + 1;
            var returnToMainMenu = totalMenuChoices;

            while (fileMenuChoice == 0)
            {
                Console.WriteLine("Choose a file to download:");

                foreach (var i in Enumerable.Range(0, fileInfoList.Count))
                {
                    var fileName = Path.GetFileName(fileInfoList[i].filePath);
                    Console.WriteLine($"{i + 1}. {fileName} ({fileInfoList[i].fileSizeInBytes.ConvertBytesForDisplay()})");
                }

                Console.WriteLine($"{returnToMainMenu}. Return to Main Menu");

                var input = Console.ReadLine();
                Console.WriteLine(string.Empty);

                var validationResult = Program.ValidateNumberIsWithinRange(input, 1, totalMenuChoices);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                fileMenuChoice = validationResult.Value;
            }

            return fileMenuChoice == returnToMainMenu
                ? Result.Fail<string>("Returning to main menu")
                : Result.Ok(fileInfoList[fileMenuChoice - 1].filePath);
        }

        private void HandleServerEvent(ServerEventInfo serverEvent)
        {
            switch (serverEvent.EventType)
            {
                case ServerEventType.ReceiveInboundFileTransferInfoCompleted:
                    Console.WriteLine("\nReceived Inbound File Transfer Request");
                    Console.WriteLine($"\tFile Name:\t\t\t{serverEvent.FileName}\n\tFile Size:\t\t\t{serverEvent.FileSizeString}\n");                    
                    break;                    

                case ServerEventType.ReceiveFileBytesStarted:
                    Console.WriteLine("\nReceving file from client...");
                    break;

                case ServerEventType.FileTransferProgress:
                    _progress.Report(serverEvent.PercentComplete);
                    break;
                    
                case ServerEventType.ReceiveFileBytesCompleted:
                    Console.WriteLine("\nSuccessfully received file from client");
                    Console.WriteLine($"\tTransfer Start Time:\t{serverEvent.FileTransferStartTime.ToLongTimeString()}\n\tTransfer Complete Time:\t{serverEvent.FileTransferCompleteTime.ToLongTimeString()}\n\tElapsed Time:\t\t\t{serverEvent.FileTransferElapsedTimeString}\n\tTransfer Rate:\t\t\t{serverEvent.FileTransferRate}\n");
                    _waitingForDownloadToComplete = false;
                    break;

                case ServerEventType.SendFileListRequestStarted:
                    Console.WriteLine($"\nSending request for list of downloadable files to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n");
                    break;

                case ServerEventType.ReceiveFileListResponseCompleted:
                    Console.WriteLine($"\nReceived list of downloadable files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.FileInfoList.Count} files in list)\n");
                    _waitingForFileListResponse = false;
                    _fileInfoList = serverEvent.FileInfoList;
                    break;

                case ServerEventType.ErrorOccurred:
                    Console.WriteLine($"Error occurred: {serverEvent.ErrorMessage}");
                    break;
            }
        }
    }
}
