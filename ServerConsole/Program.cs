using AaronLuna.Common.Network;

namespace ServerConsole
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Logging;

    using TplSocketServer;

    static class Program
    {
        static async Task Main()
        {
            Logger.LogToConsole = false;
            Logger.Start("server.log");

            var logger = new Logger(typeof(Program));
            logger.Info("Application started");

            Console.WriteLine("\nStarting asynchronous server...\n");

            var serverConsole = new ServerConsole();
            serverConsole.EventOccurred += HandleServerEvent;

            var result = await serverConsole.RunServerAsync().ConfigureAwait(false);
            if (result.Failure)
            {
                Console.WriteLine(result.Error);   
            }

            serverConsole.CloseListenSocket();
            Console.WriteLine("Press enter to exit.");
            Console.ReadLine();

            logger.Info("Application shutdown");
            Logger.ShutDown();
        }

        static void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            string fileCount;
            
            switch (serverEvent.EventType)
            {
                case EventType.ReadOutboundFileTransferInfoComplete:
                    Console.WriteLine("\nReceived Outbound File Transfer Request");
                    Console.WriteLine($"File Requested:\t\t{serverEvent.FileName}\nFile Size:\t\t{serverEvent.FileSizeString}\nRemote Endpoint:\t{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\nTarget Directory:\t{serverEvent.RemoteFolder}");
                    break;

                case EventType.ReadInboundFileTransferInfoComplete:
                    Console.WriteLine($"\nIncoming file transfer from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}:");
                    Console.WriteLine($"File Name:\t{serverEvent.FileName}\nFile Size:\t{serverEvent.FileSizeString}\nSave To:\t{serverEvent.LocalFolder}");
                    break;

                case EventType.SendNotificationNoFilesToDownloadStarted:
                    Console.WriteLine($"\nClient ({serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}) requested list of files available to download, but transfer folder is empty");
                    break;

                case EventType.ReceiveNotificationNoFilesToDownloadComplete:
                    Console.WriteLine("\nClient has no files available for download.");
                    break;

                case EventType.SendFileTransferRejectedStarted:
                    Console.WriteLine("\nA file with the same name already exists in the download folder, please rename or remove this file in order to proceed");
                    break;

                case EventType.SendFileBytesStarted:
                    Console.WriteLine("\nSending file to client...");
                    break;

                case EventType.SendFileTransferCanceledStarted:
                case EventType.ReceiveFileTransferCanceledComplete:
                    Console.WriteLine("File transfer successfully canceled");
                    break;

                case EventType.ReceiveConfirmationMessageComplete:
                    Console.WriteLine("Client confirmed file transfer completed successfully");
                    break;

                case EventType.SendFileListRequestStarted:
                    Console.WriteLine($"Sending request for list of available files to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}...");
                    break;

                case EventType.ReadFileListRequestComplete:
                    Console.WriteLine($"\nReceived request for list of available files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}...");
                    break;

                case EventType.SendFileListResponseStarted:

                    fileCount = serverEvent.FileInfoList.Count == 1
                        ? $"{serverEvent.FileInfoList.Count} file in list"
                        : $"{serverEvent.FileInfoList.Count} files in list";

                    Console.WriteLine($"Sending list of files to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({fileCount})");
                    break;

                case EventType.ReadFileListResponseComplete:

                    fileCount = serverEvent.FileInfoList.Count == 1
                        ? $"{serverEvent.FileInfoList.Count} file in list"
                        : $"{serverEvent.FileInfoList.Count} files in list";

                    Console.WriteLine($"Received list of files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({fileCount})\n");
                    break;

                case EventType.SendPublicIpRequestStarted:
                    Console.WriteLine($"\nSending request for public IP address to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}...");
                    break;

                case EventType.ReadPublicIpRequestComplete:
                    Console.WriteLine($"\nReceived request for public IP address from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}...");
                    break;

                case EventType.SendPublicIpResponseStarted:
                    Console.WriteLine($"Sending public IP address to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.PublicIpAddress})");
                    break;

                case EventType.ReadPublicIpResponseComplete:
                    Console.WriteLine($"Received public IP address from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.PublicIpAddress})\n");
                    break;

                case EventType.SendTransferFolderRequestStarted:
                    Console.WriteLine($"Sending request for transfer folder path to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}...");
                    break;

                case EventType.ReadTransferFolderRequestComplete:
                    Console.WriteLine($"\nReceived request for transfer folder path from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}...");
                    break;

                case EventType.SendTransferFolderResponseStarted:
                    Console.WriteLine($"Sending transfer folder path to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.LocalFolder})");
                    break;

                case EventType.ReadTransferFolderResponseComplete:
                    Console.WriteLine($"Received transfer folder path from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.RemoteFolder})");
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
    }
}
