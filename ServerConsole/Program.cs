namespace ServerConsole
{
    using System;
    using System.Threading.Tasks;

    using TplSocketServer;

    internal static class Program
    {
        private static async Task Main()
        {
            Console.WriteLine("\nStarting server program...\n");

            var serverProgram = new ServerProgram();
            serverProgram.EventOccurred += HandleServerEvent;

            var result = await serverProgram.RunAsyncServer().ConfigureAwait(false);
            if (result.Success)
            {
                Console.WriteLine("Press enter to exit.");
                Console.ReadLine();
            }
        }

        private static void HandleServerEvent(ServerEventInfo serverEvent)
        {
            string fileCount;

            switch (serverEvent.EventType)
            {
                case ServerEventType.ReceiveOutboundFileTransferInfoCompleted:
                    Console.WriteLine("\nReceived Outbound File Transfer Request");
                    Console.WriteLine($"File Requested:\t\t{serverEvent.FileName}\nFile Size:\t\t{serverEvent.FileSizeString}\nRemote Endpoint:\t{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\nTarget Directory:\t{serverEvent.RemoteFolder}");
                    break;

                case ServerEventType.ReceiveInboundFileTransferInfoCompleted:
                    Console.WriteLine($"\nIncoming file transfer from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}:");
                    Console.WriteLine($"File Name:\t{serverEvent.FileName}\nFile Size:\t{serverEvent.FileSizeString}\nSave To:\t{serverEvent.LocalFolder}");
                    break;

                case ServerEventType.SendFileBytesStarted:
                    Console.WriteLine("\nSending file to client...");
                    break;

                case ServerEventType.ReceiveConfirmationMessageCompleted:
                    Console.WriteLine("Client confirmed file transfer completed successfully");
                    break;

                case ServerEventType.SendFileListRequestStarted:
                    Console.WriteLine($"Sending request for list of available files to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
                    break;

                case ServerEventType.ReceiveFileListRequestCompleted:
                    Console.WriteLine($"\nReceived request for list of available files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
                    break;

                case ServerEventType.SendFileListResponseStarted:

                    fileCount = serverEvent.FileInfoList.Count == 1
                        ? $"{serverEvent.FileInfoList.Count} file in list"
                        : $"{serverEvent.FileInfoList.Count} files in list";

                    Console.WriteLine($"Sending list of files to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({fileCount})");
                    break;

                case ServerEventType.ReceiveFileListResponseCompleted:

                    fileCount = serverEvent.FileInfoList.Count == 1
                        ? $"{serverEvent.FileInfoList.Count} file in list"
                        : $"{serverEvent.FileInfoList.Count} files in list";

                    Console.WriteLine($"Received list of files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({fileCount})\n");
                    break;

                case ServerEventType.SendPublicIpRequestStarted:
                    Console.WriteLine($"\nSending request for public IP address to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
                    break;

                case ServerEventType.ReceivePublicIpRequestCompleted:
                    Console.WriteLine($"\nReceived request for public IP address from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
                    break;

                case ServerEventType.SendPublicIpResponseStarted:
                    Console.WriteLine($"Sending public IP address to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.PublicIpAddress})");
                    break;

                case ServerEventType.ReceivePublicIpResponseCompleted:
                    Console.WriteLine($"Received public IP address from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.PublicIpAddress})\n");
                    break;

                case ServerEventType.SendTransferFolderRequestStarted:
                    Console.WriteLine($"Sending request for transfer folder path to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
                    break;

                case ServerEventType.ReceiveTransferFolderRequestCompleted:
                    Console.WriteLine($"\nReceived request for transfer folder path from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
                    break;

                case ServerEventType.SendTransferFolderResponseStarted:
                    Console.WriteLine($"Sending transfer folder path to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.LocalFolder})");
                    break;

                case ServerEventType.ReceiveTransferFolderResponseCompleted:
                    Console.WriteLine($"Received transfer folder path from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.RemoteFolder})");
                    break;

                case ServerEventType.ShutdownListenSocketCompleted:
                    Console.WriteLine("Server has been successfully shutdown, press Enter to exit program\n");
                    break;

                case ServerEventType.ErrorOccurred:
                    Console.WriteLine($"Error encountered while processing request from client:\n{serverEvent.ErrorMessage}");
                    break;
            }
        }
    }
}
