namespace TplSocketServer
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    using AaronLuna.Common.Extensions;
    using AaronLuna.Common.IO;

    public class ServerEvent
    {
        public EventType EventType { get; set; }
        public int BytesReceived { get; set; }
        public int ExpectedByteCount { get; set; }
        public int UnreadByteCount { get; set; }
        public int MessageLengthInBytes { get; set; }
        public int CurrentMessageBytesReceived { get; set; }
        public int TotalMessageBytesReceived { get; set; }
        public int MessageBytesRemaining { get; set; }
        public MessageType MessageType { get; set; }
        public string TextMessage { get; set; }
        public string RemoteServerIpAddress { get; set; }
        public int RemoteServerPortNumber { get; set; }
        public string LocalIpAddress { get; set; }
        public int LocalPortNumber { get; set; }
        public string PublicIpAddress { get; set; }
        public string LocalFolder { get; set; }
        public string RemoteFolder { get; set; }
        public string FileName { get; set; }
        public long FileSizeInBytes { get; set; }
        public string FileSizeString => FileHelper.FileSizeToString(FileSizeInBytes);
        public List<(string, long)> FileInfoList { get; set; }
        public DateTime FileTransferStartTime { get; set; }
        public DateTime FileTransferCompleteTime { get; set; }
        public TimeSpan FileTransferTimeSpan => FileTransferCompleteTime - FileTransferStartTime;
        public string FileTransferElapsedTimeString => FileTransferTimeSpan.ToFormattedString();
        public string FileTransferRate => FileHelper.GetTransferRate(FileTransferTimeSpan, FileSizeInBytes);
        public int CurrentFileBytesReceived { get; set; }
        public long TotalFileBytesReceived { get; set; }
        public int CurrentFileBytesSent { get; set; }        
        public long FileBytesRemaining { get; set; }
        public int SocketReadCount { get;set; }
        public int SocketSendCount { get; set; }
        public int FileChunkSentCount { get; set; }
        public float PercentComplete { get; set; }
        public string ConfirmationMessage { get; set; }
        public string ErrorMessage { get; set; }

        public override string ToString()
        {
            return Report();
        }

        string Report()
        {
            var report = string.Empty;
            switch (EventType)
            {
                case EventType.ListenOnLocalPortStarted:
                    report += "Started Process:\t\tListen on local port";
                    break;

                case EventType.ListenOnLocalPortComplete:
                    report += "Completed Process:\tListen on local port";
                    break;

                case EventType.EnterMainAcceptConnectionLoop:
                    report += "\t\t\t\t\t\tEntering main loop to accept incoming connection requests";
                    break;

                case EventType.ExitMainAcceptConnectionLoop:
                    report += "\t\t\t\t\t\tExiting main loop to accept incoming connection requests";
                    break;

                case EventType.AcceptConnectionAttemptStarted:
                    report += "Started Process:\t\tAccept connection from remote client";
                    break;

                case EventType.AcceptConnectionAttemptComplete:
                    report += $"Completed Process:\tAccept connection from remote client ({RemoteServerIpAddress})";
                    break;

                case EventType.ConnectToRemoteServerStarted:
                    report += "Started Process:\t\tConnect to remote server";
                    break;

                case EventType.ConnectToRemoteServerComplete:
                    report += "Completed Process:\tConnect to remote server";
                    break;

                case EventType.ReceiveMessageFromClientStarted:
                    report += "Started Process:\t\tReceive message from client";
                    break;

                case EventType.ReceiveMessageFromClientComplete:
                    report += "Completed Process:\tReceive message from client";
                    break;

                case EventType.DetermineMessageLengthStarted:
                    report += "Started Process:\t\tDetermine message length";
                    break;

                case EventType.DetermineMessageLengthComplete:
                    report += $"Completed Process:\tDetermine message length ({MessageLengthInBytes:N0} bytes)";
                    break;

                case EventType.ReceivedMessageLengthFromSocket:
                    report += $"\t\t\t\t\t\tReceived Data From Socket:{Environment.NewLine}{Environment.NewLine}\tBytes Received:\t\t{BytesReceived:N0}{Environment.NewLine}\tExpected Bytes:\t\t{MessageLengthInBytes:N0}{Environment.NewLine}\tUnread Bytes:\t\t{UnreadByteCount:N0}{Environment.NewLine}";
                    break;

                case EventType.SaveUnreadBytesAfterReceiveMessageLength:
                    report += $"\t\t\t\t\t\tSocket receive operation returned {UnreadByteCount:N0} more bytes than expected (Int32 = 4 bytes)";
                    break;

                case EventType.CopySavedBytesToMessageData:
                    report += $"\t\t\t\t\t\tProcessed unread bytes as message data:{Environment.NewLine}{Environment.NewLine}\tUnread Bytes:\t\t{UnreadByteCount:N0}{Environment.NewLine}\tMessage Length:\t\t{MessageLengthInBytes:N0}{Environment.NewLine}\tBytes Remaining:\t{MessageBytesRemaining:N0}{Environment.NewLine}";
                    break;

                case EventType.ReceivedMessageBytesFromSocket:
                    report += $"\t\t\t\t\t\tReceived Data From Socket:{Environment.NewLine}{Environment.NewLine}\tSocket Read Count:\t\t\t{SocketReadCount:N0}{Environment.NewLine}\tBytes Received:\t\t\t\t{BytesReceived:N0}{Environment.NewLine}\tMessage Bytes (Current):\t{CurrentMessageBytesReceived:N0}{Environment.NewLine}\tMessage Bytes (Total):\t\t{TotalMessageBytesReceived:N0}{Environment.NewLine}\tMessage Length:\t\t\t\t{MessageLengthInBytes:N0}{Environment.NewLine}\tBytes Remaining:\t\t\t{MessageBytesRemaining:N0}{Environment.NewLine}\tUnread Bytes:\t\t\t\t{UnreadByteCount:N0}{Environment.NewLine}";
                    break;

                case EventType.ReceiveMessageBytesStarted:
                    report += "Started Process:\t\tReceive message bytes";
                    break;

                case EventType.ReceiveMessageBytesComplete:
                    report += "Completed Process:\tReceive message bytes";
                    break;

                case EventType.SaveUnreadBytesAfterReceiveMessage:
                    report += $"\t\t\t\t\t\tSocket receive operation returned {UnreadByteCount:N0} more bytes than expected";
                    break;

                case EventType.DetermineMessageTypeStarted:
                    report += "Started Process:\t\tDetermine message type";
                    break;

                case EventType.DetermineMessageTypeComplete:
                    report += $"Completed Process:\tDetermine message type{Environment.NewLine}{Environment.NewLine}\tMessage Type:\t{MessageType}{Environment.NewLine}";
                    break;

                case EventType.ShutdownListenSocketStarted:
                    report += "Started Process:\t\tShutdown listening socket";
                    break;

                case EventType.ShutdownListenSocketCompletedWithoutError:
                    report += "Completed Process:\tShutdown listening socket";
                    break;

                case EventType.ShutdownListenSocketCompletedWithError:
                    report += $"Error occurred while attempting to shutdown listening socket:{Environment.NewLine}{ErrorMessage}";
                    break;

                case EventType.SendTextMessageStarted:
                    report += $"Started Process:\tSend Text Message{Environment.NewLine}{Environment.NewLine}\tSend Message To:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tMessage:\t\t\t{TextMessage}{Environment.NewLine}";
                    break;

                case EventType.SendTextMessageComplete:
                    report += "Completed Process:\tSend Text Message";
                    break;

                case EventType.ReadTextMessageStarted:
                    report += "Started Process:\t\tRead Text Message";
                    break;

                case EventType.ReadTextMessageComplete:
                    report += $"Completed Process:\tRead Text Message{Environment.NewLine}{Environment.NewLine}\tMessage From:\t\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tMessage:\t{TextMessage}{Environment.NewLine}";
                    break;

                case EventType.SendPublicIpRequestStarted:
                    report += $"Started Process:\tSend request for public IP address{Environment.NewLine}{Environment.NewLine}\tSend Request To:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tRequested By:\t\t{LocalIpAddress}:{LocalPortNumber}{Environment.NewLine}";
                    break;

                case EventType.SendPublicIpRequestComplete:
                    report += "Completed Process:\tSend request for public IP address";
                    break;

                case EventType.ReadPublicIpRequestStarted:
                    report += "Started Process:\t\tRead request for public IP address";
                    break;

                case EventType.ReadPublicIpRequestComplete:
                    report += $"Completed Process:\tRead request for public IP address{Environment.NewLine}{Environment.NewLine}\tSend Response To:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}";
                    break;

                case EventType.SendPublicIpResponseStarted:
                    report += $"Started Process:\tSend public IP address response{Environment.NewLine}{Environment.NewLine}\tSend Response To:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tPublic IP Address:\t{PublicIpAddress}{Environment.NewLine}";
                    break;

                case EventType.SendPublicIpResponseComplete:
                    report += "Completed Process:\tSend public IP address response";
                    break;

                case EventType.ReadPublicIpResponseStarted:
                    report += "Started Process:\t\tRead public IP address response";
                    break;

                case EventType.ReadPublicIpResponseComplete:
                    report += $"Completed Process:\tRead public IP address response{Environment.NewLine}{Environment.NewLine}\tSent From:\t\t\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tPublic IP Address:\t{PublicIpAddress}{Environment.NewLine}";
                    break;

                case EventType.SendTransferFolderRequestStarted:
                    report += $"Started Process:\tSend request for transfer folder path{Environment.NewLine}{Environment.NewLine}\tSend Request To:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tRequested By:\t\t{LocalIpAddress}:{LocalPortNumber}{Environment.NewLine}";
                    break;

                case EventType.SendTransferFolderRequestComplete:
                    report += "Completed Process:\tSend request for transfer folder path";
                    break;

                case EventType.ReadTransferFolderRequestStarted:
                    report += "Started Process:\t\tRead request for transfer folder path";
                    break;

                case EventType.ReadTransferFolderRequestComplete:
                    report += $"Completed Process:\tRead request for transfer folder path{Environment.NewLine}{Environment.NewLine}\tSend Response To:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}";
                    break;

                case EventType.SendTransferFolderResponseStarted:
                    report += $"Started Process:\tSend transfer folder response{Environment.NewLine}{Environment.NewLine}\tSend Response To:\t\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tTransfer Folder Path:\t{LocalFolder}{Environment.NewLine}";
                    break;

                case EventType.SendTransferFolderResponseComplete:
                    report += "Completed Process:\tSend transfer folder response";
                    break;

                case EventType.ReadTransferFolderResponseStarted:
                    report += "Started Process:\t\tRead transfer folder response";
                    break;

                case EventType.ReadTransferFolderResponseComplete:
                    report += $"Completed Process:\tRead transfer folder response{Environment.NewLine}{Environment.NewLine}\tSent From:\t\t\t\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tTransfer Folder Path:\t{RemoteFolder}{Environment.NewLine}";
                    break;

                case EventType.SendFileListRequestStarted:
                    report += $"Started Process:\tSend File List Request{Environment.NewLine}{Environment.NewLine}\tSend Request To:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tRequested By:\t\t{LocalIpAddress}:{LocalPortNumber}{Environment.NewLine}\tTarget Folder:\t\t{RemoteFolder}{Environment.NewLine}";
                    break;

                case EventType.SendFileListRequestComplete:
                    report += "Completed Process:\tSend File List Request";
                    break;

                case EventType.ReadFileListRequestStarted:
                    report += "Started Process:\t\tRead File List Request";
                    break;

                case EventType.ReadFileListRequestComplete:
                    report += $"Completed Process:\tRead File List Request{Environment.NewLine}{Environment.NewLine}\tSend Response To:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tTarget Folder:\t\t{RemoteFolder}{Environment.NewLine}";
                    break;

                case EventType.SendFileListResponseStarted:
                    report += $"Started Process:\tSend File List Response{Environment.NewLine}{Environment.NewLine}\tSend Response To:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tFile Info:\t\t\t{FileInfoList.Count} files available{Environment.NewLine}";
                    break;

                case EventType.SendFileListResponseComplete:
                    report += "Completed Process:\tSend File List Response";
                    break;

                case EventType.ReadFileListResponseStarted:
                    report += "Started Process:\t\tRead File List Response";
                    break;

                case EventType.ReadFileListResponseComplete:
                    report += $"Completed Process:\tRead File List Response{Environment.NewLine}{Environment.NewLine}\tSent From:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tFile Info:\t{FileInfoList.Count} files available{Environment.NewLine}";
                    break;

                case EventType.SendInboundFileTransferInfoStarted:
                    report += $"Started Process:\tSend request to retrieve file from remote host{Environment.NewLine}{Environment.NewLine}\tRequest File From:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tSend File To:\t\t{LocalIpAddress}:{LocalPortNumber}{Environment.NewLine}\tFile Name:\t\t\t{FileName}{Environment.NewLine}\tFile Location:\t\t{RemoteFolder}{Environment.NewLine}\tTarget Folder:\t\t{LocalFolder}{Environment.NewLine}";
                    break;

                case EventType.SendInboundFileTransferInfoComplete:
                    report += "Completed Process:\tSend request to retrieve file from remote host";
                    break;

                case EventType.ReadInboundFileTransferInfoStarted:
                    report += "Started Process:\t\tRead request to receive file from remote host";
                    break;

                case EventType.ReadInboundFileTransferInfoComplete:
                    report += $"Completed Process:\tRead request to receive file from remote host{Environment.NewLine}{Environment.NewLine}\tFile Sent From:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tFile Name:\t\t{FileName}{Environment.NewLine}\tFile Size:\t\t{FileSizeInBytes:N0} bytes ({FileSizeString}){Environment.NewLine}\tTarget Folder:\t{LocalFolder}{Environment.NewLine}";
                    break;

                case EventType.SendOutboundFileTransferInfoStarted:
                    report += $"Started Process:\tSend request to transfer file to remote host{Environment.NewLine}{Environment.NewLine}\tSend File To:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tFile Name:\t\t{FileName}{Environment.NewLine}\tFile Size:\t\t{FileSizeInBytes:N0} bytes ({FileSizeString}){Environment.NewLine}\tFile Location:\t{LocalFolder}{Environment.NewLine}\tTarget Folder:\t{RemoteFolder}{Environment.NewLine}";
                    break;

                case EventType.SendOutboundFileTransferInfoComplete:
                    report += "Completed Process:\tSend request to transfer file to remote host";
                    break;

                case EventType.ReadOutboundFileTransferInfoStarted:
                    report += "Started Process:\t\tRead request to transfer file to remote host";
                    break;

                case EventType.ReadOutboundFileTransferInfoComplete:
                    report += $"Completed Process:\tRead request to transfer file to remote host{Environment.NewLine}{Environment.NewLine}\tSend File To:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}\tFile Name:\t\t{FileName}{Environment.NewLine}\tFile Size:\t\t{FileSizeInBytes:N0} bytes ({FileSizeString}){Environment.NewLine}\tFile Location:\t{LocalFolder}{Environment.NewLine}\tTarget Folder:\t{RemoteFolder}{Environment.NewLine}";
                    break;

                case EventType.SendFileTransferAcceptedStarted:
                    report += $"Started Process:\tNotify client that file transfer has been accepted";
                    break;

                case EventType.SendFileTransferAcceptedComplete:
                    report += "Completed Process:\tNotify client that file transfer has been accepted";
                    break;

                case EventType.ReceiveFileTransferAcceptedStarted:
                    report += "Started Process:\t\tReceive notification that file transfer has been accepted";
                    break;

                case EventType.ReceiveFileTransferAcceptedComplete:
                    report += "Completed Process:\tReceive notification that file transfer has been accepted";
                    break;

                case EventType.SendFileTransferRejectedStarted:
                    report += $"Started Process:\tNotify client that file transfer was rejected, file with same name already exists at location {LocalFolder}{Path.DirectorySeparatorChar}{FileName}";
                    break;

                case EventType.SendFileTransferRejectedComplete:
                    report += "Completed Process:\tNotify client that file transfer was rejected";
                    break;

                case EventType.ReceiveFileTransferRejectedStarted:
                    report += "Started Process:\t\tReceive notification that file transfer was rejected";
                    break;

                case EventType.ReceiveFileTransferRejectedComplete:
                    report += "Completed Process:\tReceive notification that file transfer was rejected";
                    break;

                case EventType.SendFileBytesStarted:
                    report += "Started Process:\t\tSend file bytes";
                    break;

                case EventType.SentFileChunkToClient:
                    report += $"\t\t\t\t\t\tSent file chunk #{FileChunkSentCount:N0} to client ({SocketSendCount:N0} total Socket.Send calls):{Environment.NewLine}{Environment.NewLine}\tCurrent Bytes Sent:\t\t{CurrentFileBytesSent:N0}{Environment.NewLine}\tBytes Remaining:\t\t{FileBytesRemaining:N0}{Environment.NewLine}\tFile Size In Bytes:\t\t{FileSizeInBytes:N0}{Environment.NewLine}";
                    break;

                case EventType.SendFileBytesComplete:
                    report += "Completed Process:\tSend file bytes";
                    break;

                case EventType.CopySavedBytesToIncomingFile:
                    report += $"\t\t\t\t\t\tProcessed unread bytes as file data{Environment.NewLine}{Environment.NewLine}\tSaved Bytes:\t\t{CurrentFileBytesReceived:N0}{Environment.NewLine}\tFile Size:\t\t\t{FileSizeInBytes:N0}{Environment.NewLine}\tBytes Remaining:\t{FileBytesRemaining:N0}{Environment.NewLine}";
                    break;

                case EventType.ReceiveFileBytesStarted:
                    report += "Started Process:\t\tReceive file bytes";
                    break;

                case EventType.ReceivedFileBytesFromSocket:
                    report += $"\t\t\t\t\t\tReceived Data From Socket:{Environment.NewLine}{Environment.NewLine}\tSocket Read Count:\t\t{SocketReadCount:N0}{Environment.NewLine}\tBytes Received:\t\t\t{BytesReceived:N0}{Environment.NewLine}\tFile Bytes (Current):\t{CurrentFileBytesReceived:N0}{Environment.NewLine}\tFile Bytes (Total):\t\t{TotalFileBytesReceived:N0}{Environment.NewLine}\tFile Size:\t\t\t\t{FileSizeInBytes:N0}{Environment.NewLine}\tBytes Remaining:\t\t{FileBytesRemaining:N0}{Environment.NewLine}";
                    break;

                case EventType.UpdateFileTransferProgress:
                    report += $"\t\t\t\t\t\tFile Transfer Progress Update:\t{PercentComplete:P0.00} Complete";
                    break;

                case EventType.ReceiveFileBytesComplete:
                    report += $"Completed Process:\tReceive file bytes{Environment.NewLine}{Environment.NewLine}\tDownload Started:\t{FileTransferStartTime.ToLongTimeString()}{Environment.NewLine}\tDownload Finished:\t{FileTransferCompleteTime.ToLongTimeString()}{Environment.NewLine}\tElapsed Time:\t\t{FileTransferElapsedTimeString}{Environment.NewLine}\tTransfer Rate:\t\t{FileTransferRate}{Environment.NewLine}";
                    break;

                case EventType.SendConfirmationMessageStarted:
                    report += "Started Process:\t\tSend Confirmation Message";
                    break;

                case EventType.SendConfirmationMessageComplete:
                    report += "Completed Process:\tSend confirmation message";
                    break;

                case EventType.ReceiveConfirmationMessageStarted:
                    report += "Started Process:\t\tReceive confirmation message";
                    break;

                case EventType.ReceiveConfirmationMessageComplete:
                    report += "Completed Process:\tReceive confirmation message";
                    break;

                case EventType.SendFileTransferStalledStarted:
                    report += $"Started Process:\tNotify client that inbound file transfer has stalled ({RemoteServerIpAddress}:{RemoteServerPortNumber})";
                    break;

                case EventType.SendFileTransferStalledComplete:
                    report += "Completed Process:\tNotify client that inbound file transfer has stalled";
                    break;

                case EventType.ReceiveFileTransferStalledStarted:
                    report += "Started Process:\t\tReceive notification that file transfer is incomplete and data has stopped being received";
                    break;

                case EventType.ReceiveFileTransferStalledComplete:
                    report += $"Completed Process:\tReceive notification that file transfer is incomplete and data has stopped being received ({RemoteServerIpAddress}:{RemoteServerPortNumber})";
                    break;

                //case EventType.SendFileTransferCanceledStarted:
                //    report += $"Started Process:\tNotify client that the file transfer has been canceled ({RemoteServerIpAddress}:{RemoteServerPortNumber})";
                //    break;

                //case EventType.SendFileTransferCanceledComplete:
                //    report += "Completed Process:\tNotify client that the file transfer has been canceled";
                //    break;

                //case EventType.ReceiveFileTransferCanceledStarted:
                //    report += "Started Process:\t\tReceive notification that the file transfer was canceled by the remote host";
                //    break;

                //case EventType.ReceiveFileTransferCanceledComplete:
                //    report += $"Completed Process:\tReceive notification that the file transfer was canceled by the remote host ({RemoteServerIpAddress}:{RemoteServerPortNumber})";
                //    break;

                case EventType.SendRetryOutboundFileTransferStarted:
                    report += $"Started Process:\tSend request to retry stalled/canceled file transfer to remote host ({RemoteServerIpAddress}:{RemoteServerPortNumber})";
                    break;

                case EventType.SendRetryOutboundFileTransferComplete:
                    report += "Completed Process:\tSend request to retry stalled/canceled file transfer to remote host";
                    break;

                case EventType.ReceiveRetryOutboundFileTransferStarted:
                    report += "Started Process:\t\tReceive request to retry stalled/canceled file transfer from client";
                    break;

                case EventType.ReceiveRetryOutboundFileTransferComplete:
                    report += $"Completed Process:\tReceive request to retry stalled/canceled file transfer from client ({RemoteServerIpAddress}:{RemoteServerPortNumber})";
                    break;

                case EventType.SendNotificationNoFilesToDownloadStarted:
                    report += $"Started Process:\tNotify client that no files are available to download from the requested folder ({RemoteServerIpAddress}:{RemoteServerPortNumber})";
                    break;

                case EventType.SendNotificationNoFilesToDownloadComplete:
                    report += "Completed Process:\tNotify client that no files are available to download from the requested folder";
                    break;

                case EventType.ReceiveNotificationNoFilesToDownloadStarted:
                    report += "Started Process:\t\tReceive notification that no files are available to download from the requested folder";
                    break;

                case EventType.ReceiveNotificationNoFilesToDownloadComplete:
                    report += $"Completed Process:\tReceive notification that no files are available to download from the requested folder ({RemoteServerIpAddress}:{RemoteServerPortNumber})";
                    break;

                case EventType.SendNotificationFolderDoesNotExistStarted:
                    report += $"Started Process:\tNotify client that the requested folder does not exist ({RemoteServerIpAddress}:{RemoteServerPortNumber})";
                    break;

                case EventType.SendNotificationFolderDoesNotExistComplete:
                    report += "Completed Process:\tNotify client that the requested folder does not exist";
                    break;

                case EventType.ReceiveNotificationFolderDoesNotExistStarted:
                    report += "Started Process:\t\tReceive notification that the requested folder does not exist";
                    break;

                case EventType.ReceiveNotificationFolderDoesNotExistComplete:
                    report += $"Completed Process:\tReceive notification that the requested folder does not exist ({RemoteServerIpAddress}:{RemoteServerPortNumber})";
                    break;

                case EventType.SendShutdownServerCommandStarted:
                    report += $"Started Process:\tSend shutdown server command started";
                    break;

                case EventType.SendShutdownServerCommandComplete:
                    report += "Completed Process:\tSend shutdown server command complete";
                    break;

                case EventType.ReceiveShutdownServerCommandStarted:
                    report += "Started Process:\t\tReceive shutdown server command started";
                    break;

                case EventType.ReceiveShutdownServerCommandComplete:
                    report += $"Completed Process:\tReceive shutdown server command complete";
                    break;

                case EventType.ErrorOccurred:
                    report += $"Error Occurred!{Environment.NewLine}{Environment.NewLine}\t{ErrorMessage}{Environment.NewLine}";
                    break;
            }

            return report;
        }
    }
}
