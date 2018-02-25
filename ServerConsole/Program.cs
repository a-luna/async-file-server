namespace ServerConsole
{
    using System;
    using System.Threading.Tasks;

    using TplSocketServer;

    class Program
    {
        static async Task Main()
        {
            var serverProgram = new ServerProgram();
            serverProgram.EventOccurred += HandleServerEvent;

            await serverProgram.RunAsyncServer();

            Console.WriteLine("Press enter to exit.");
            Console.ReadLine();
        }

        private static void HandleServerEvent(ServerEventInfo serverEvent)
        {
            switch (serverEvent.EventType)
            {
                case ServerEventType.ReceiveTextMessageCompleted:
                    Console.WriteLine($"\nMessage received from client {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}:");
                    Console.WriteLine($"{serverEvent.TextMessage}\n");
                    break;

                case ServerEventType.ReceiveOutboundFileTransferInfoCompleted:
                    Console.WriteLine("\nReceived Outbound File Transfer Request");
                    Console.WriteLine($"\tFile Requested:\t\t{serverEvent.FileName}\n\tFile Size:\t\t{serverEvent.FileSizeString}\n\tRemote Endpoint:\t{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n\tTarget Directory:\t{serverEvent.RemoteFolder}\n");
                    break;

                case ServerEventType.ReceiveInboundFileTransferInfoCompleted:
                    Console.WriteLine("\nReceived Inbound File Transfer Request");
                    Console.WriteLine($"\tFile Name:\t\t\t{serverEvent.FileName}\n\tFile Size:\t\t\t{serverEvent.FileSizeString}\n");                    
                    break;

                case ServerEventType.SendFileBytesStarted:
                    Console.WriteLine("\nSending file to client...");
                    break;

                case ServerEventType.ReceiveFileBytesStarted:
                    Console.WriteLine("\nReceving file from client...");
                    break;

                case ServerEventType.FileTransferProgress:
                    Console.WriteLine($"\nFile Transfer {serverEvent.PercentComplete:P0} Complete\n");
                    break;

                case ServerEventType.ReceiveConfirmationMessageCompleted:
                    Console.WriteLine("Client confirmed file transfer completed successfully\n");                    
                    break;

                case ServerEventType.ReceiveFileBytesCompleted:
                    Console.WriteLine("\nSuccessfully received file from client");
                    Console.WriteLine($"\tTransfer Start Time:\t{serverEvent.FileTransferStartTime.ToLongTimeString()}\n\tTransfer Complete Time:\t{serverEvent.FileTransferCompleteTime.ToLongTimeString()}\n\tElapsed Time:\t\t\t{serverEvent.FileTransferElapsedTimeString}\n\tTransfer Rate:\t\t\t{serverEvent.FileTransferRate}\n");                    
                    break;

                case ServerEventType.SendFileListRequestStarted:
                    Console.WriteLine($"\nSending request for list of downloadable files to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n");
                    break;

                case ServerEventType.ReceiveFileListRequestCompleted:
                    Console.WriteLine($"\nReceived request for list of downloadable files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
                    break;

                case ServerEventType.SendFileListResponseStarted:
                    Console.WriteLine($"Sending list of downloadable files to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.FileInfoList.Count} files in list)");
                    break;

                case ServerEventType.ReceiveFileListResponseCompleted:
                    Console.WriteLine($"\nReceived list of downloadable files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.FileInfoList.Count} files in list)\n");
                    break;

                case ServerEventType.SendPublicIpRequestStarted:
                    Console.WriteLine($"\nSending request for public IP address to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n");
                    break;

                case ServerEventType.ReceivePublicIpRequestCompleted:
                    Console.WriteLine($"\nReceived request for public IP address from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
                    break;

                case ServerEventType.SendPublicIpResponseStarted:
                    Console.WriteLine($"Sending public IP address to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.PublicIpAddress})");
                    break;

                case ServerEventType.ReceivePublicIpResponseCompleted:
                    Console.WriteLine($"\nReceived public IP address from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.PublicIpAddress})\n");                    
                    break;

                case ServerEventType.SendTransferFolderRequestStarted:
                    Console.WriteLine($"\nSending request for transfer folder path to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n");
                    break;

                case ServerEventType.ReceiveTransferFolderRequestCompleted:
                    Console.WriteLine($"\nReceived request for transfer folder path from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
                    break;

                case ServerEventType.SendTransferFolderResponseStarted:
                    Console.WriteLine($"Sending transfer folder path to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.LocalFolder})");
                    break;

                case ServerEventType.ReceiveTransferFolderResponseCompleted:
                    Console.WriteLine($"\nReceived transfer folder path from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.RemoteFolder})\n");
                    break;

                case ServerEventType.ShutdownListenSocketCompleted:
                    Console.WriteLine("\nServer has been successfully shutdown\n");
                    break;

                case ServerEventType.ErrorOccurred:
                    Console.WriteLine($"Error occurred: {serverEvent.ErrorMessage}");
                    break;
            }
        }
    }
}
