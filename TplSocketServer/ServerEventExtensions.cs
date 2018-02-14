namespace TplSocketServer
{
    using System;

    public static class ServerEventExtensions
    {
        public static string Report(this ServerEvent serverEvent)
        {
            var report = $"{DateTime.Now.ToLongTimeString()}\t";

            switch (serverEvent.EventType)
            {
                case ServerEventType.ListenOnLocalPortStarted:
                    report += "Started Process: Listen on local port";
                    break;

                case ServerEventType.ListenOnLocalPortCompleted:
                    report += "Completed Process: Listen on local port";
                    break;

                case ServerEventType.ConnectionAttemptStarted:
                    report += "Started Process: Accept connection";
                    break;

                case ServerEventType.ConnectionAttemptCompleted:
                    report += "Completed Process: Accept connection";
                    break;

                case ServerEventType.ConnectToRemoteServerStarted:
                    report += "Started Process: Connect to remote server";
                    break;

                case ServerEventType.ConnectToRemoteServerCompleted:
                    report += "Completed Process: Connect to remote server";
                    break;

                case ServerEventType.DetermineTransferTypeStarted:
                    report += "Started Process: Determine transfer type";
                    break;

                case ServerEventType.DetermineTransferTypeCompleted:
                    report += "Completed Process: Determine transfer type" + Environment.NewLine
                             + $"\tTransfer Type:\t{serverEvent.TransferType}";
                    break;

                case ServerEventType.ShutdownListenSocketStarted:
                    report += "Started Process: Shutdown listening socket";
                    break;

                case ServerEventType.ShutdownListenSocketCompleted:
                    report += "Completed Process: Shutdown listening socket";
                    break;

                case ServerEventType.SendTextMessageStarted:
                    report += $"Started Process: Send Text Message\n\tMessage:\t{serverEvent.TextMessage}\n\tRemote Endpoint:\t{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}";
                    break;

                case ServerEventType.SendTextMessageCompleted:
                    report += "Completed Process: Send Text Message";
                    break;

                case ServerEventType.SendInboundFileTransferInfoStarted:
                    report += $"Started Process: Send inbound transfer info\n\tRequesting File:\t{serverEvent.RemoteFolder}\\{serverEvent.FileName}\n\tRemote Endpoint:\t{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n\tDownload Folder:\t{serverEvent.LocalFolder}";
                    break;

                case ServerEventType.SendInboundFileTransferInfoCompleted:
                    report += "Completed Process: Send inbound transfer info";
                    break;

                case ServerEventType.SendOutboundFileTransferInfoStarted:
                    report += $"Started Process: Send outbound transfer info\n\tSending File:\t\t{serverEvent.LocalFolder}\\{serverEvent.FileName}\n\tFile Size:\t\t\t{serverEvent.FileSizeString}\n\tRemote Endpoint:\t{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n\tTarget Directory:\t{serverEvent.RemoteFolder}";
                    break;

                case ServerEventType.SendOutboundFileTransferInfoCompleted:
                    report += "Completed Process: Send outbound transfer info";
                    break;

                case ServerEventType.ReceiveTextMessageStarted:
                    report += "Started Process: Receive Text Message";
                    break;

                case ServerEventType.ReceiveTextMessageCompleted:
                    report += $"Completed Process: Receive Text Message\n\tMessage:\t{serverEvent.TextMessage}\n\tSent From:\t{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}";
                    break;

                case ServerEventType.ReceiveInboundFileTransferInfoStarted:
                    report += "Started Process: Receive inbound transfer info";
                    break;

                case ServerEventType.ReceiveInboundFileTransferInfoCompleted:
                    report =
                        $"Completed Process: Receive inbound transfer info\n\tDownload Location:\t{serverEvent.LocalFolder}\n\tFile Name:\t\t\t{serverEvent.FileName}\n\tFile Size:\t\t\t{serverEvent.FileSizeString}";
                    break;

                case ServerEventType.ReceiveOutboundFileTransferInfoStarted:
                    report += "Started Process: Receive outbound transfer info";
                    break;

                case ServerEventType.ReceiveOutboundFileTransferInfoCompleted:
                    report =
                        $"Completed Process: Receive outbound transfer info\n\tFile Requested:\t\t{serverEvent.LocalFolder}\\{serverEvent.FileName}\n\tFile Size:\t\t{serverEvent.FileSizeString}\n\tRemote Endpoint:\t{serverEvent.RemoteServerIpAddress}:{serverEvent.RemoteServerPortNumber}\n\tTarget Directory:\t{serverEvent.RemoteFolder}";
                    break;

                case ServerEventType.SendFileBytesStarted:
                    report += "Started Process: Send file bytes";
                    break;

                case ServerEventType.SendFileBytesCompleted:
                    report += "Completed Process: Send file bytes";
                    break;

                case ServerEventType.ReceiveFileBytesStarted:
                    report += $"Started Process: Receive file bytes\n\tTransfer Start Time:\t{serverEvent.FileTransferStartTime.ToLongTimeString()}";
                    break;

                case ServerEventType.ReceivedDataFromSocket:
                    report =
                        $"Received Data From Socket:\n\tData Received Count:\t{serverEvent.ReceiveBytesCount}\n\tCurrent Bytes Received:\t{serverEvent.CurrentBytesReceivedFromSocket}\n\tTotal Bytes Received:\t{serverEvent.TotalBytesReceivedFromSocket}\n\tFile Size In Bytes:\t{serverEvent.FileSizeInBytes}\n\tBytes Remaining:\t{serverEvent.BytesRemainingInFile}";
                    break;

                case ServerEventType.FileTransferProgress:
                    report += $"File Transfer In Progress:\t{serverEvent.PercentComplete:P0} Complete";
                    break;

                case ServerEventType.ReceiveFileBytesCompleted:
                    report += $"Completed Process: Receive file bytes\n\tTransfer Start Time:\t{serverEvent.FileTransferStartTime.ToLongTimeString()}\n\tTransfer Complete Time:\t{serverEvent.FileTransferCompleteTime.ToLongTimeString()}\n\tElapsed Time:\t\t\t{serverEvent.FileTransferElapsedTimeString}\n\tTransfer Rate:\t\t{serverEvent.FileTransferRate}";
                    break;

                case ServerEventType.SendConfirmationMessageStarted:
                    report += $"Started Process: Send Confirmation Message\n\tConfirmation Message:\t{serverEvent.ConfirmationMessage}";
                    break;

                case ServerEventType.SendConfirmationMessageCompleted:
                    report += "Completed Process: Send confirmation message";
                    break;

                case ServerEventType.ReceiveConfirmationMessageStarted:
                    report += "Started Process: Receive confirmation message";
                    break;

                case ServerEventType.ReceiveConfirmationMessageCompleted:
                    report += $"Completed Process: Receive confirmation message\n\tConfirmation Message:\t{serverEvent.ConfirmationMessage}";
                    break;

                case ServerEventType.CloseTransferSocketStarted:
                    report += "Started Process: Close transfer socket";
                    break;

                case ServerEventType.CloseTransferSocketCompleted:
                    report += "Completed Process: Close transfer socket";
                    break;

                case ServerEventType.ErrorOccurred:
                    report += $"Error Occurred!\n\t{serverEvent.ErrorMessage}";
                    break;
            }

            return report;
        }
    }
}
