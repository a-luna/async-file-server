namespace TplSocketServer
{
    using System;

    public static class ServerEventInfoExtensions
    {
        public static string Report(this ServerEventInfo serverEventInfo)
        {
            var report = $"\t{DateTime.Now.ToLongTimeString()}\t";

            switch (serverEventInfo.EventType)
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
                    report += $"Completed Process: Determine transfer typee\n\n\tTransfer Type:\t{serverEventInfo.RequestType}\n";
                    break;

                case ServerEventType.ShutdownListenSocketStarted:
                    report += "Started Process: Shutdown listening socket";
                    break;

                case ServerEventType.ShutdownListenSocketCompleted:
                    report += "Completed Process: Shutdown listening socket";
                    break;

                case ServerEventType.SendTextMessageStarted:
                    report += $"Started Process: Send Text Message\n\n\tMessage:\t{serverEventInfo.TextMessage}\n\tSend To:\t\t{serverEventInfo.RemoteServerIpAddress}:{serverEventInfo.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.SendTextMessageCompleted:
                    report += "Completed Process: Send Text Message";
                    break;

                case ServerEventType.SendFileListRequestStarted:
                    report += $"Started Process: Send File List Request\n\n\tServer Endpoint:\t\t{serverEventInfo.RemoteServerIpAddress}:{serverEventInfo.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.SendFileListRequestCompleted:
                    report += "Completed Process: Send File List Request";
                    break;

                case ServerEventType.ReceiveFileListRequestStarted:
                    report += "Started Process: Receive File List Request";
                    break;

                case ServerEventType.ReceiveFileListRequestCompleted:
                    report += $"Completed Process: Receive File List Request\n\n\tServer Endpoint:\t\t{serverEventInfo.RemoteServerIpAddress}:{serverEventInfo.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.SendFileListResponseStarted:
                    report += $"Started Process: Send File List Response\n\n\tFile Info List:\t\t{serverEventInfo.FileInfoList.Count} files available\n\tServer Endpoint:\t\t{serverEventInfo.RemoteServerIpAddress}:{serverEventInfo.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.SendFileListResponseCompleted:
                    report += "Completed Process: Send File List Response";
                    break;

                case ServerEventType.ReceiveFileListResponseStarted:
                    report += "Started Process: Receive File List Response";
                    break;

                case ServerEventType.ReceiveFileListResponseCompleted:
                    report += $"Completed Process: Receive File List Response\n\n\tFile Info List:\t\t{serverEventInfo.FileInfoList.Count} files available\n\tServer Endpoint:\t\t{serverEventInfo.RemoteServerIpAddress}:{serverEventInfo.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.SendInboundFileTransferInfoStarted:
                    report += $"Started Process: Send inbound transfer info\n\n\tRequesting File:\t\t{serverEventInfo.FileName}\n\tServer Endpoint:\t\t{serverEventInfo.RemoteServerIpAddress}:{serverEventInfo.RemoteServerPortNumber}\n\tDownload Folder:\t\t{serverEventInfo.LocalFolder}\n";
                    break;

                case ServerEventType.SendInboundFileTransferInfoCompleted:
                    report += "Completed Process: Send inbound transfer info";
                    break;

                case ServerEventType.SendOutboundFileTransferInfoStarted:
                    report += $"Started Process: Send outbound transfer info\n\n\tSending File:\t\t{serverEventInfo.FileName}\n\tFile Size:\t\t\t{serverEventInfo.FileSizeString}\n\tClient Endpoint:\t\t{serverEventInfo.RemoteServerIpAddress}:{serverEventInfo.RemoteServerPortNumber}\n\tTarget Directory:\t\t{serverEventInfo.RemoteFolder}\n";
                    break;

                case ServerEventType.SendOutboundFileTransferInfoCompleted:
                    report += "Completed Process: Send outbound transfer info";
                    break;

                case ServerEventType.ReceiveTextMessageStarted:
                    report += "Started Process: Receive Text Message";
                    break;

                case ServerEventType.ReceiveTextMessageCompleted:
                    report += $"Completed Process: Receive Text Message\n\n\tMessage:\t{serverEventInfo.TextMessage}\n\tSent From:\t{serverEventInfo.RemoteServerIpAddress}:{serverEventInfo.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.ReceiveInboundFileTransferInfoStarted:
                    report += "Started Process: Receive inbound transfer info";
                    break;

                case ServerEventType.ReceiveInboundFileTransferInfoCompleted:
                    report +=
                        $"Completed Process: Receive inbound transfer info\n\n\tFile Name:\t\t{serverEventInfo.FileName}\n\tFile Size:\t\t\t{serverEventInfo.FileSizeString}\n\tDownload Location:\t{serverEventInfo.LocalFolder}\n";
                    break;

                case ServerEventType.ReceiveOutboundFileTransferInfoStarted:
                    report += "Started Process: Receive outbound transfer info";
                    break;

                case ServerEventType.ReceiveOutboundFileTransferInfoCompleted:
                    report +=
                        $"Completed Process: Receive outbound transfer info\n\n\tFile Requested:\t\t{serverEventInfo.FileName}\n\tFile Size:\t\t\t{serverEventInfo.FileSizeString}\n\tClient Endpoint:\t\t{serverEventInfo.RemoteServerIpAddress}:{serverEventInfo.RemoteServerPortNumber}\n\tTarget Directory:\t\t{serverEventInfo.RemoteFolder}\n";
                    break;

                case ServerEventType.SendFileBytesStarted:
                    report += "Started Process: Send file bytes";
                    break;

                case ServerEventType.SendFileBytesCompleted:
                    report += "Completed Process: Send file bytes";
                    break;

                case ServerEventType.ReceiveFileBytesStarted:
                    report += $"Started Process: Receive file bytes";
                    break;

                case ServerEventType.ReceivedDataFromSocket:
                    report +=
                        $"Received Data From Socket:\n\n\tData Received Count:\t{serverEventInfo.ReceiveBytesCount}\n\tCurrent Bytes Received:\t{serverEventInfo.CurrentBytesReceivedFromSocket}\n\tTotal Bytes Received:\t{serverEventInfo.TotalBytesReceivedFromSocket}\n\tFile Size In Bytes:\t{serverEventInfo.FileSizeInBytes}\n\tBytes Remaining:\t{serverEventInfo.BytesRemainingInFile}\n";
                    break;

                case ServerEventType.FileTransferProgress:
                    report += $"File Transfer In Progress:\t{serverEventInfo.PercentComplete:P0} Complete";
                    break;

                case ServerEventType.ReceiveFileBytesCompleted:
                    report += $"Completed Process: Receive file bytes\n\n\tDownload Started:\t{serverEventInfo.FileTransferStartTime.ToLongTimeString()}\n\tDownload Finished:\t{serverEventInfo.FileTransferCompleteTime.ToLongTimeString()}\n\tElapsed Time:\t\t{serverEventInfo.FileTransferElapsedTimeString}\n\tTransfer Rate:\t\t{serverEventInfo.FileTransferRate}\n";
                    break;

                case ServerEventType.SendConfirmationMessageStarted:
                    report += $"Started Process: Send Confirmation Message\n\tConfirmation Message:\t{serverEventInfo.ConfirmationMessage}";
                    break;

                case ServerEventType.SendConfirmationMessageCompleted:
                    report += "Completed Process: Send confirmation message";
                    break;

                case ServerEventType.ReceiveConfirmationMessageStarted:
                    report += "Started Process: Receive confirmation message";
                    break;

                case ServerEventType.ReceiveConfirmationMessageCompleted:
                    report += $"Completed Process: Receive confirmation message\n\tConfirmation Message:\t{serverEventInfo.ConfirmationMessage}";
                    break;

                case ServerEventType.CloseTransferSocketStarted:
                    report += "Started Process: Close transfer socket";
                    break;

                case ServerEventType.CloseTransferSocketCompleted:
                    report += "Completed Process: Close transfer socket";
                    break;

                case ServerEventType.ErrorOccurred:
                    report += $"Error Occurred!\n\t{serverEventInfo.ErrorMessage}";
                    break;
            }

            return report;
        }
    }
}
