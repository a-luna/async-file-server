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
                    Console.WriteLine($"{serverEvent.TextMessage}");
                    break;

                case ServerEventType.ReceiveOutboundFileTransferInfoCompleted:
                    Console.WriteLine("\nReceived Outbound File Transfer Request");
                    Console.WriteLine($"File Requested:\t\t{serverEvent.FileName}\nFile Size:\t\t{serverEvent.FileSizeString}\nRemote Endpoint:\t{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\nTarget Directory:\t{serverEvent.RemoteFolder}");
                    break;

                case ServerEventType.SendFileBytesStarted:
                    Console.WriteLine("\nSending file to client...");
                    break;

                case ServerEventType.ReceiveFileBytesStarted:
                    Console.WriteLine("\nReceiving file from client...");
                    break;

                case ServerEventType.ReceiveConfirmationMessageCompleted:
                    Console.WriteLine("Client confirmed file transfer completed successfully");                    
                    break;

                case ServerEventType.SendFileListRequestStarted:
                    Console.WriteLine($"Sending request for list of available files to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
                    break;

                case ServerEventType.ReceiveFileListRequestCompleted:
                    Console.WriteLine($"Received request for list of available files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
                    break;

                case ServerEventType.SendFileListResponseStarted:
                    Console.WriteLine($"Sending list of downloadable files to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.FileInfoList.Count} files in list)");
                    break;

                case ServerEventType.ReceiveFileListResponseCompleted:
                    Console.WriteLine($"\nReceived list of downloadable files from {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber} ({serverEvent.FileInfoList.Count} files in list)\n");
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
                    Console.WriteLine($"\nSending request for transfer folder path to {serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}");
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
                    Console.WriteLine("\nServer has been successfully shutdown\n");
                    break;

                case ServerEventType.ErrorOccurred:
                    Console.WriteLine($"Error occurred: {serverEvent.ErrorMessage}");
                    break;
            }
        }
    }
}
