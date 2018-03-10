namespace TplSocketServer
{
    using System;

    public static class ServerEventInfoExtensions
    {
        public static string Report(this ServerEventArgs serverEventArgs)
        {
            var reportTime = DateTime.Now;

            var report = $"\t{reportTime.ToLongTimeString()} {reportTime.Millisecond}ms\t";

            switch (serverEventArgs.EventType)
            {
                case ServerEventType.ListenOnLocalPortStarted:
                    report += "Started Process: Listen on local port";
                    break;

                case ServerEventType.ListenOnLocalPortCompleted:
                    report += "Completed Process: Listen on local port";
                    break;

                case ServerEventType.AcceptConnectionAttemptStarted:
                    report += "Started Process: Accept connection";
                    break;

                case ServerEventType.AcceptConnectionAttemptCompleted:
                    report += "Completed Process: Accept connection";
                    break;

                case ServerEventType.ConnectToRemoteServerStarted:
                    report += "Started Process: Connect to remote server";
                    break;

                case ServerEventType.ConnectToRemoteServerCompleted:
                    report += "Completed Process: Connect to remote server";
                    break;

                case ServerEventType.DetermineMessageLengthStarted:
                    report += "Started Process: Determine incoming message length";
                    break;

                case ServerEventType.DetermineMessageLengthCompleted:
                    report += $"Completed Process: Determine incoming message length\n\n\tMessage Length:\t\t\t{serverEventArgs.MessageLength}\n\tUnread Byte Count:\t\t{serverEventArgs.UnreadByteCount}\n";
                    break;

                case ServerEventType.ReceiveAllMessageBytesStarted:
                    report += "Started Process: Receive incoming message bytes";
                    break;

                case ServerEventType.ReceiveAllMessageBytesCompleted:
                    report += $"Completed Process: Receive incoming message bytes\n\n\tUnread Byte Count:\t{serverEventArgs.UnreadByteCount}\n";
                    break;

                case ServerEventType.DetermineRequestTypeStarted:
                    report += "Started Process: Determine request type";
                    break;

                case ServerEventType.DetermineRequestTypeCompleted:
                    report += $"Completed Process: Determine reqeust type\n\n\tTransfer Type:\t{serverEventArgs.RequestType}\n";
                    break;

                case ServerEventType.ShutdownListenSocketStarted:
                    report += "Started Process: Shutdown listening socket";
                    break;

                case ServerEventType.ShutdownListenSocketCompleted:
                    report += "Completed Process: Shutdown listening socket";
                    break;

                case ServerEventType.SendTextMessageStarted:
                    report += $"Started Process: Send Text Message\n\n\tMessage:\t{serverEventArgs.TextMessage}\n\tSend To:\t\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.SendTextMessageCompleted:
                    report += "Completed Process: Send Text Message";
                    break;

                case ServerEventType.SendPublicIpRequestStarted:
                    report += $"Started Process: Send Public IP Request\n\n\tServer Endpoint:\t\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.SendPublicIpRequestCompleted:
                    report += "Completed Process: Send Public IP Request";
                    break;

                case ServerEventType.ReceivePublicIpRequestStarted:
                    report += "Started Process: Receive Public IP Request";
                    break;

                case ServerEventType.ReceivePublicIpRequestCompleted:
                    report += $"Completed Process: Receive Public IP Request\n\n\tServer Endpoint:\t\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.SendPublicIpResponseStarted:
                    report += $"Started Process: Send Public IP Response\n\n\tPublic IP:\t\t{serverEventArgs.PublicIpAddress} files available\n\tServer Endpoint:\t\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}";
                    break;

                case ServerEventType.SendPublicIpResponseCompleted:
                    report += "Completed Process: Send Public IP Response";
                    break;

                case ServerEventType.ReceivePublicIpResponseStarted:
                    report += "Started Process: Receive Public IP Response";
                    break;

                case ServerEventType.ReceivePublicIpResponseCompleted:
                    report += $"Completed Process: Receive Public IP Response\n\n\tPublic IP:\t\t{serverEventArgs.PublicIpAddress} files available\n\tServer Endpoint:\t\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}";
                    break;

                case ServerEventType.SendTransferFolderRequestStarted:
                    report += $"Started Process: Send Transfer Folder Request\n\n\tServer Endpoint:\t\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.SendTransferFolderRequestCompleted:
                    report += "Completed Process: Send Transfer Folder Request";
                    break;

                case ServerEventType.ReceiveTransferFolderRequestStarted:
                    report += "Started Process: Receive Transfer Folder Request";
                    break;

                case ServerEventType.ReceiveTransferFolderRequestCompleted:
                    report += $"Completed Process: Receive Transfer Folder Request\n\n\tServer Endpoint:\t\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.SendTransferFolderResponseStarted:
                    report += $"Started Process: Send Transfer Folder Response\n\n\tTransfer Folder Path:\t{serverEventArgs.LocalFolder}\n\tServer Endpoint:\t\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.SendTransferFolderResponseCompleted:
                    report += "Completed Process: Send Transfer Folder Response";
                    break;

                case ServerEventType.ReceiveTransferFolderResponseStarted:
                    report += "Started Process: Receive Transfer Folder Response";
                    break;

                case ServerEventType.ReceiveTransferFolderResponseCompleted:
                    report += $"Completed Process: Receive Transfer Folder Response\n\n\tTransfer Folder Path:\t{serverEventArgs.RemoteFolder}\n\tServer Endpoint:\t\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.SendFileListRequestStarted:
                    report += $"Started Process: Send File List Request\n\n\tServer Endpoint:\t\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.SendFileListRequestCompleted:
                    report += "Completed Process: Send File List Request";
                    break;

                case ServerEventType.ReceiveFileListRequestStarted:
                    report += "Started Process: Receive File List Request";
                    break;

                case ServerEventType.ReceiveFileListRequestCompleted:
                    report += $"Completed Process: Receive File List Request\n\n\tServer Endpoint:\t\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.SendFileListResponseStarted:
                    report += $"Started Process: Send File List Response\n\n\tFile Info List:\t\t{serverEventArgs.FileInfoList.Count} files available\n\tServer Endpoint:\t\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n\tTarget Folder:\t\t{serverEventArgs.LocalFolder}";
                    break;

                case ServerEventType.SendFileListResponseCompleted:
                    report += "Completed Process: Send File List Response";
                    break;

                case ServerEventType.ReceiveFileListResponseStarted:
                    report += "Started Process: Receive File List Response";
                    break;

                case ServerEventType.ReceiveFileListResponseCompleted:
                    report += $"Completed Process: Receive File List Response\n\n\tFile Info List:\t\t{serverEventArgs.FileInfoList.Count} files available\n\tServer Endpoint:\t\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n\tTarget Folder:\t\t{serverEventArgs.LocalFolder}";
                    break;

                case ServerEventType.SendInboundFileTransferInfoStarted:
                    report += $"Started Process: Send inbound transfer info\n\n\tRequesting File:\t\t{serverEventArgs.FileName}\n\tServer Endpoint:\t\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n\tDownload Folder:\t\t{serverEventArgs.LocalFolder}\n";
                    break;

                case ServerEventType.SendInboundFileTransferInfoCompleted:
                    report += "Completed Process: Send inbound transfer info";
                    break;

                case ServerEventType.SendOutboundFileTransferInfoStarted:
                    report += $"Started Process: Send outbound transfer info\n\n\tSending File:\t\t{serverEventArgs.FileName}\n\tFile Size:\t\t\t{serverEventArgs.FileSizeString}\n\tClient Endpoint:\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n\tTarget Directory:\t{serverEventArgs.RemoteFolder}\n";
                    break;

                case ServerEventType.SendOutboundFileTransferInfoCompleted:
                    report += "Completed Process: Send outbound transfer info";
                    break;

                case ServerEventType.ReceiveTextMessageStarted:
                    report += "Started Process: Receive Text Message";
                    break;

                case ServerEventType.ReceiveTextMessageCompleted:
                    report += $"Completed Process: Receive Text Message\n\n\tMessage:\t{serverEventArgs.TextMessage}\n\tSent From:\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n";
                    break;

                case ServerEventType.ReceiveInboundFileTransferInfoStarted:
                    report += "Started Process: Receive inbound transfer info";
                    break;

                case ServerEventType.ReceiveInboundFileTransferInfoCompleted:
                    report +=
                        $"Completed Process: Receive inbound transfer info\n\n\tFile Name:\t\t\t{serverEventArgs.FileName}\n\tFile Size:\t\t\t{serverEventArgs.FileSizeString}\n\tDownload Location:\t{serverEventArgs.LocalFolder}\n";
                    break;

                case ServerEventType.ReceiveOutboundFileTransferInfoStarted:
                    report += "Started Process: Receive outbound transfer info";
                    break;

                case ServerEventType.ReceiveOutboundFileTransferInfoCompleted:
                    report +=
                        $"Completed Process: Receive outbound transfer info\n\n\tFile Requested:\t\t{serverEventArgs.FileName}\n\tFile Size:\t\t\t{serverEventArgs.FileSizeString}\n\tClient Endpoint:\t{serverEventArgs.RemoteServerIpAddress}:{serverEventArgs.RemoteServerPortNumber}\n\tTarget Directory:\t{serverEventArgs.RemoteFolder}\n";
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
                        $"Received Data From Socket:\n\n\tData Received Count:\t{serverEventArgs.ReceiveBytesCount}\n\tCurrent Bytes Received:\t{serverEventArgs.CurrentBytesReceivedFromSocket}\n\tTotal Bytes Received:\t{serverEventArgs.TotalBytesReceivedFromSocket}\n\tFile Size In Bytes:\t\t{serverEventArgs.FileSizeInBytes}\n\tBytes Remaining:\t\t{serverEventArgs.BytesRemainingInFile}\n";
                    break;

                case ServerEventType.FileTransferProgress:
                    report += $"File Transfer In Progress:\t{serverEventArgs.PercentComplete:P0} Complete";
                    break;

                case ServerEventType.ReceiveFileBytesCompleted:
                    report += $"Completed Process: Receive file bytes\n\n\tDownload Started:\t{serverEventArgs.FileTransferStartTime.ToLongTimeString()}\n\tDownload Finished:\t{serverEventArgs.FileTransferCompleteTime.ToLongTimeString()}\n\tElapsed Time:\t\t{serverEventArgs.FileTransferElapsedTimeString}\n\tTransfer Rate:\t\t{serverEventArgs.FileTransferRate}\n";
                    break;

                case ServerEventType.SendConfirmationMessageStarted:
                    report += $"Started Process: Send Confirmation Message\n\tConfirmation Message:\t{serverEventArgs.ConfirmationMessage}";
                    break;

                case ServerEventType.SendConfirmationMessageCompleted:
                    report += "Completed Process: Send confirmation message";
                    break;

                case ServerEventType.ReceiveConfirmationMessageStarted:
                    report += "Started Process: Receive confirmation message";
                    break;

                case ServerEventType.ReceiveConfirmationMessageCompleted:
                    report += $"Completed Process: Receive confirmation message\n\tConfirmation Message:\t{serverEventArgs.ConfirmationMessage}";
                    break;

                case ServerEventType.NotifyClientDataIsNoLongerBeingReceivedStarted:
                    report += "Started Process: Notify client that file transfer data is no longer being received";
                    break;

                case ServerEventType.NotifyClientDataIsNoLongerBeingReceivedCompleted:
                    report += "Completed Process: Notify client that file transfer data is no longer being received";
                    break;

                case ServerEventType.AbortOutboundFileTransfer:
                    report += "Abort outbound file transfer";
                    break;

                case ServerEventType.ShutdownTransferSocketStarted:
                    report += "Started Process: Close transfer socket";
                    break;

                case ServerEventType.ShutdownTransferSocketCompleted:
                    report += "Completed Process: Close transfer socket";
                    break;

                case ServerEventType.ErrorOccurred:
                    report += $"Error Occurred!\n\t{serverEventArgs.ErrorMessage}";
                    break;
            }

            return report;
        }
    }
}
