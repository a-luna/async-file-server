namespace TplSockets
{
    using System;
    using System.IO;
    using System.Net;

    using AaronLuna.Common.Extensions;
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Numeric;

    public class ServerEvent
    {

        public EventType EventType { get; set; }

        public IPAddress RemoteServerIpAddress { get; set; }
        public int RemoteServerPortNumber { get; set; }
        public IPAddress LocalIpAddress { get; set; }
        public int LocalPortNumber { get; set; }
        public IPAddress PublicIpAddress { get; set; }

        public int BytesReceived { get; set; }
        public int ExpectedByteCount { get; set; }
        public int UnreadByteCount { get; set; }
        public int MessageLengthInBytes { get; set; }
        public byte[] MessageLengthData { get; set; }
        public int CurrentMessageBytesReceived { get; set; }
        public int TotalMessageBytesReceived { get; set; }
        public int MessageBytesRemaining { get; set; }
        public byte[] MessageData { get; set; }
        public RequestType RequestType { get; set; }
        public int RequestId { get; set; }

        public string TextMessage { get; set; }

        public FileInfoList RemoteServerFileList { get; set; }

        public int FileTransferId { get; set; }
        public int RetryCounter { get; set; }
        public int RetryLimit { get; set; }
        public bool RetryLimitExceeded { get; set; }
        public string LocalFolder { get; set; }
        public string RemoteFolder { get; set; }
        public string FileName { get; set; }
        public long FileSizeInBytes { get; set; }
        public string FileSizeString => FileHelper.FileSizeToString(FileSizeInBytes);
        public DateTime FileTransferStartTime { get; set; }
        public DateTime FileTransferCompleteTime { get; set; }
        public DateTime RetryLockoutExpireTime { get; set; }
        public TimeSpan FileTransferTimeSpan => FileTransferCompleteTime - FileTransferStartTime;
        public string FileTransferElapsedTimeString => FileTransferTimeSpan.ToFormattedString();
        public string FileTransferRate => FileTransfer.GetTransferRate(FileTransferTimeSpan, FileSizeInBytes);
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
            const string indentLevel1 = "\t\t\t\t\t\t\t\t\t\t";
            var report = string.Empty;
            switch (EventType)
            {
                case EventType.ServerStartedListening:
                    report += $"NOW ACCEPTING CONNECTIONS ON PORT {LocalPortNumber}";
                    break;

                case EventType.ServerStoppedListening:
                    report += "SERVER SHUTDOWN COMPLETE, NO LONGER ACCEPTING CONNECTIONS";
                    break;

                case EventType.ConnectionAccepted:
                    report += $"Connection accepted from {RemoteServerIpAddress}";
                    break;

                case EventType.ConnectToRemoteServerStarted:
                    report +=
                        $"START PROCESS: CONNECT TO SERVER: {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.ConnectToRemoteServerComplete:
                    report += $"PROCESS COMPLETE: CONNECT TO SERVER: {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.ReceiveMessageFromClientStarted:
                    report += $"START PROCESS: RECEIVE MESSAGE FROM CLIENT: {RemoteServerIpAddress}";
                    break;

                case EventType.ReceiveMessageFromClientComplete:
                    report += $"PROCESS COMPLETE: RECEIVE MESSAGE FROM CLIENT: {RemoteServerIpAddress}";
                    break;

                case EventType.DetermineMessageLengthStarted:
                    report += "Step 1: Determine request length from first 4 bytes received";
                    break;

                case EventType.DetermineMessageLengthComplete:
                    report += $"Incoming request length: {MessageLengthInBytes:N0} bytes ({MessageLengthData.ToHexString()})";
                    break;

                case EventType.ReceivedMessageLengthFromSocket:
                    report +=
                        $"Received data from socket:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Received:\t\t{BytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Expected Bytes:\t\t{MessageLengthInBytes:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Unread Bytes:\t\t{UnreadByteCount:N0}{Environment.NewLine}";
                    break;

                case EventType.SaveUnreadBytesAfterReceiveMessageLength:
                case EventType.SaveUnreadBytesAfterReceiveMessage:
                    report +=
                        $"Socket.Receive operation returned {UnreadByteCount:N0} more bytes than expected";
                    break;

                case EventType.CopySavedBytesToMessageData:
                    report +=
                        $"Processed unread bytes as request data:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Unread Bytes:\t\t{UnreadByteCount:N0}{Environment.NewLine}" +
                        $"{indentLevel1}ServerRequest Length:\t\t{MessageLengthInBytes:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Remaining:\t{MessageBytesRemaining:N0}{Environment.NewLine}";
                    break;

                case EventType.ReceivedMessageBytesFromSocket:
                    report +=
                        $"Received data from socket:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Socket Read Count:\t\t\t{SocketReadCount:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Received:\t\t\t\t{BytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}ServerRequest Bytes (Current):\t{CurrentMessageBytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}ServerRequest Bytes (Total):\t\t{TotalMessageBytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}ServerRequest Length:\t\t\t\t{MessageLengthInBytes:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Remaining:\t\t\t{MessageBytesRemaining:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Unread Bytes:\t\t\t\t{UnreadByteCount:N0}{Environment.NewLine}";
                    break;

                case EventType.ReceiveMessageBytesStarted:
                    report += "Step 2: Receive request bytes";
                    break;

                case EventType.ReceiveMessageBytesComplete:
                    report += "Successfully received all request bytes";
                    break;

                case EventType.ProcessRequestStarted:
                    report += $"START PROCESS: {RequestType.Name()}";
                    break;

                case EventType.ProcessRequestComplete:
                    report += $"PROCESS COMPLETE: {RequestType.Name()}";
                    break;

                case EventType.ShutdownListenSocketStarted:
                    report += "Attempting to shutdown listening socket...";
                    break;

                case EventType.ShutdownListenSocketCompletedWithoutError:
                    report += "Successfully shutdown listening socket";
                    break;

                case EventType.ShutdownListenSocketCompletedWithError:
                    report +=
                        $"Error occurred while attempting to shutdown listening socket: {ErrorMessage}";
                    break;

                case EventType.SendTextMessageStarted:
                    report +=
                        $"Sending text message to {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Message:\t{TextMessage}{Environment.NewLine}";
                    break;

                case EventType.SendTextMessageComplete:
                    report += "Text message was successfully sent";
                    break;

                case EventType.ReceivedTextMessage:
                    report +=
                        $"Text message received{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Message From:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}Message:\t\t{TextMessage}{Environment.NewLine}";
                    break;

                case EventType.RequestServerInfoStarted:
                    report +=
                        $"Sending request for server connection info to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.RequestServerInfoComplete:
                case EventType.RequestFileListComplete:
                case EventType.RequestInboundFileTransferComplete:
                case EventType.RequestOutboundFileTransferComplete:
                case EventType.RetryOutboundFileTransferComplete:
                    report += "Request was successfully sent";
                    break;

                case EventType.ReceivedServerInfoRequest:
                case EventType.ReceivedRetryOutboundFileTransferRequest:
                    report += $"Requested by: {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.SendServerInfoStarted:
                    report +=
                        $"Sending server connection info to {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}";
                    break;

                case EventType.SendServerInfoComplete:
                    report += "Server connection info was successfully sent";
                    break;
                    
                case EventType.RequestFileListStarted:
                    report +=
                        $"Sending request for available file information to {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Requested By:\t\t{LocalIpAddress}:{LocalPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}Target Folder:\t\t{RemoteFolder}{Environment.NewLine}";
                    break;

                case EventType.ReceivedFileListRequest:
                    report += $"File list request details{Environment.NewLine}{Environment.NewLine}" +
                              $"{indentLevel1}Send Response To:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}" +
                              $"{indentLevel1}Target Folder:\t\t{RemoteFolder}{Environment.NewLine}";
                    break;

                case EventType.SendFileListStarted:
                    report +=
                        $"Sending requested file information to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.SendFileListComplete:
                    report += "File information was successfully sent";
                    break;

                case EventType.ReceivedFileList:
                    report +=
                        $"File list received from {RemoteServerIpAddress}:{RemoteServerPortNumber}, {RemoteServerFileList.Count} files available";
                    break;

                case EventType.RequestInboundFileTransferStarted:
                    report +=
                        $"Sending inbound file transfer request to {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Requested By:\t\t{LocalIpAddress}:{LocalPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}File Name:\t\t\t{FileName}{Environment.NewLine}" +
                        $"{indentLevel1}File Location:\t\t{RemoteFolder}{Environment.NewLine}" +
                        $"{indentLevel1}Target Folder:\t\t{LocalFolder}{Environment.NewLine}";
                    break;

                case EventType.ReceivedInboundFileTransferRequest:
                    report +=
                        $"File transfer request details{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}File Sender:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}File Name:\t\t{FileName}{Environment.NewLine}" +
                        $"{indentLevel1}File Size:\t\t{FileSizeInBytes:N0} bytes ({FileSizeString}){Environment.NewLine}" +
                        $"{indentLevel1}Target Folder:\t{LocalFolder}{Environment.NewLine}";
                    break;

                case EventType.RequestOutboundFileTransferStarted:
                    report +=
                        $"Sending outbound file transfer request to {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}File Name:\t\t{FileName}{Environment.NewLine}" +
                        $"{indentLevel1}File Size:\t\t{FileSizeInBytes:N0} bytes ({FileSizeString}){Environment.NewLine}" +
                        $"{indentLevel1}File Location:\t{LocalFolder}{Environment.NewLine}" +
                        $"{indentLevel1}Target Folder:\t{RemoteFolder}{Environment.NewLine}";
                    break;

                case EventType.ReceivedOutboundFileTransferRequest:
                    report +=
                        $"File transfer request details{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Send File To:\t{RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}File Name:\t\t{FileName}{Environment.NewLine}" +
                        $"{indentLevel1}File Size:\t\t{FileSizeInBytes:N0} bytes ({FileSizeString}){Environment.NewLine}" +
                        $"{indentLevel1}File Location:\t{LocalFolder}{Environment.NewLine}" +
                        $"{indentLevel1}Target Folder:\t{RemoteFolder}{Environment.NewLine}";
                    break;

                case EventType.SendFileTransferAcceptedStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that file transfer has been accepted";
                    break;

                case EventType.SendFileTransferAcceptedComplete:
                case EventType.SendFileTransferRejectedComplete:
                case EventType.SendFileTransferStalledComplete:
                case EventType.SendNotificationNoFilesToDownloadComplete:
                case EventType.SendNotificationFolderDoesNotExistComplete:
                    report += "Notification was successfully sent";
                    break;

                case EventType.RemoteServerAcceptedFileTransfer:
                    report += $"Outbound file transfer accepted by {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.SendFileTransferRejectedStarted:
                    report +=
                        $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that file transfer has been rejected, " +
                        $"file with same name already exists at location {LocalFolder}{Path.DirectorySeparatorChar}{FileName}";
                    break;

                case EventType.RemoteServerRejectedFileTransfer:
                    report += $"Outbound file transfer rejected by {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.SendFileBytesStarted:
                    report += $"Sending file to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.SentFileChunkToClient:
                    report +=
                        $"Sent file chunk #{FileChunkSentCount:N0} ({SocketSendCount:N0} total Socket.Send calls):{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Current Bytes Sent:\t\t{CurrentFileBytesSent:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Remaining:\t\t{FileBytesRemaining:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Size In Bytes:\t\t{FileSizeInBytes:N0}{Environment.NewLine}";
                    break;

                case EventType.SendFileBytesComplete:
                    report += $"Successfully sent file to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.CopySavedBytesToIncomingFile:
                    report +=
                        $"Processed unread bytes as file data{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Saved Bytes:\t\t{CurrentFileBytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Size:\t\t\t{FileSizeInBytes:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Remaining:\t{FileBytesRemaining:N0}{Environment.NewLine}";
                    break;

                case EventType.ReceiveFileBytesStarted:
                    report += $"Receiving file from {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.ReceivedFileBytesFromSocket:
                    report +=
                        $"Received Data From Socket:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Socket Read Count:\t\t{SocketReadCount:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Received:\t\t\t{BytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Bytes (Current):\t{CurrentFileBytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Bytes (Total):\t\t{TotalFileBytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Size:\t\t\t\t{FileSizeInBytes:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Remaining:\t\t{FileBytesRemaining:N0}{Environment.NewLine}";
                    break;

                case EventType.UpdateFileTransferProgress:
                    report += $"File Transfer Progress Update: {PercentComplete:P2} Complete";
                    break;

                case EventType.ReceiveFileBytesComplete:
                    report +=
                        $"Successfully received file from {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Download Started:\t{FileTransferStartTime:MM/dd/yyyy HH:mm:ss.fff}{Environment.NewLine}" +
                        $"{indentLevel1}Download Finished:\t{FileTransferCompleteTime:MM/dd/yyyy HH:mm:ss.fff}{Environment.NewLine}" +
                        $"{indentLevel1}Elapsed Time:\t\t{FileTransferElapsedTimeString}{Environment.NewLine}" +
                        $"{indentLevel1}Transfer Rate:\t\t{FileTransferRate}{Environment.NewLine}";
                    break;

                case EventType.SendConfirmationMessageStarted:
                    report += $"Sending confirmation message to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.SendConfirmationMessageComplete:
                    report += "Confirmation message was successfully sent";
                    break;

                case EventType.ReceiveConfirmationMessageStarted:
                    report += $"Waiting for {RemoteServerIpAddress}:{RemoteServerPortNumber} to confirm file transfer was successful";
                    break;

                case EventType.ReceiveConfirmationMessageComplete:
                    report += $"{RemoteServerIpAddress}:{RemoteServerPortNumber} confirmed file transfer was successful";
                    break;

                case EventType.SendFileTransferStalledStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that file transfer has stalled";
                    break;

                case EventType.FileTransferStalled:
                    report += $"Received notification from {RemoteServerIpAddress}:{RemoteServerPortNumber} that file transfer is incomplete and data has stopped being received";
                    break;

                case EventType.RetryOutboundFileTransferStarted:
                    report += $"Sending request to retry unsuccessful file transfer to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.SendNotificationNoFilesToDownloadStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that no files are available to download from the requested folder";
                    break;

                case EventType.ReceivedNotificationNoFilesToDownload:
                    report += $"Received notification from {RemoteServerIpAddress}:{RemoteServerPortNumber} that no files are available to download from the requested folder";
                    break;

                case EventType.SendNotificationFolderDoesNotExistStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that the requested folder does not exist";
                    break;

                case EventType.ReceivedNotificationFolderDoesNotExist:
                    report += $"Received notification from {RemoteServerIpAddress}:{RemoteServerPortNumber} that the requested folder does not exist";
                    break;

                case EventType.SendShutdownServerCommandStarted:
                    report += "START PROCESS: INITIATE SERVER SHUTDOWN";
                    break;

                case EventType.SendShutdownServerCommandComplete:
                    report += "PROCESS COMPLETE: INITIATE SERVER SHUTDOWN";
                    break;

                case EventType.ReceivedShutdownServerCommand:
                    report += $"Shutdown command was received from {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.ErrorOccurred:
                    report += $"Error Occurred!{Environment.NewLine}{Environment.NewLine}\t{ErrorMessage}{Environment.NewLine}";
                    break;
            }

            return report;
        }
    }
}
