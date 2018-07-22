namespace AaronLuna.AsyncFileServer.Model
{
    using System;
    using System.Net;

    using Common.Extensions;
    using Common.IO;
    using Common.Numeric;

    public class ServerEvent
    {
        public ServerEvent()
        {
            TimeStamp = DateTime.Now;
        }

        public DateTime TimeStamp { get; private set; }
        public ServerEventType EventType { get; set; }

        public IPAddress RemoteServerIpAddress { get; set; }
        public int RemoteServerPortNumber { get; set; }
        public ServerPlatform RemoteServerPlatform { get; set; }
        public IPAddress LocalIpAddress { get; set; }
        public int LocalPortNumber { get; set; }
        public IPAddress PublicIpAddress { get; set; }

        public int BytesReceivedCount { get; set; }
        public int ExpectedByteCount { get; set; }
        public int UnreadBytesCount { get; set; }
        public byte[] RequestLengthBytes { get; set; }
        public int RequestLengthInBytes { get; set; }
        public int CurrentRequestBytesReceived { get; set; }
        public int TotalRequestBytesReceived { get; set; }
        public int RequestBytesRemaining { get; set; }
        public byte[] RequestBytes { get; set; }
        public ServerRequestType RequestType { get; set; }
        public int RequestId { get; set; }
        public int ItemsInQueueCount { get; set; }

        public int TextSessionId { get; set; }
        public string TextMessage { get; set; }

        public FileInfoList RemoteServerFileList { get; set; }

        public int FileTransferId { get; set; }
        public FileTransferStatus FileTransferStatusCurrent { get; set; }
        public FileTransferStatus FileTransferStatusPrevious { get; set; }
        public int RetryCounter { get; set; }
        public int RemoteServerRetryLimit { get; set; }
        public string LocalFolder { get; set; }
        public string RemoteFolder { get; set; }
        public string FileName { get; set; }
        public long FileSizeInBytes { get; set; }
        public string FileSizeString => FileHelper.FileSizeToString(FileSizeInBytes);
        public DateTime FileTransferStartTime { get; set; }
        public DateTime FileTransferCompleteTime { get; set; }
        public DateTime RetryLockoutExpireTime { get; set; }
        public string RetryLockoutTimeRemianing => (RetryLockoutExpireTime - DateTime.Now).ToFormattedString();
        public TimeSpan FileTransferDuration => FileTransferCompleteTime - FileTransferStartTime;
        public string FileTransferDurationTimeString => FileTransferDuration.ToFormattedString();
        public string FileTransferRate { get; set; }
        public int CurrentFileBytesReceived { get; set; }
        public long TotalFileBytesReceived { get; set; }
        public int CurrentFileBytesSent { get; set; }
        public long FileBytesRemaining { get; set; }
        public int SocketReadCount { get;set; }
        public int SocketSendCount { get; set; }
        public int FileChunkSentCount { get; set; }
        public int FileWriteAttempts { get; set; }
        public float PercentComplete { get; set; }
        public string ConfirmationMessage { get; set; }
        public string ErrorMessage { get; set; }

        public bool DoNotDisplayInLog => EventType.DoNotDisplayInLog();
        public bool LogLevelIsDebugOnly => EventType.LogLevelIsDebugOnly();

        public void UpdateTimeStamp()
        {
            TimeStamp = DateTime.Now;
        }

        public string GetLogFileEntry()
        {
            return Report(false);
        }

        public override string ToString()
        {
            return $"[{TimeStamp:MM/dd/yyyy HH:mm:ss.fff}] {Report(true)}";
        }

        string Report(bool logToScreen)
        {
            var indentLevel1 = logToScreen
                ? string.Empty
                : "\t\t\t\t\t\t\t\t\t";

            var report = string.Empty;
            switch (EventType)
            {
                case ServerEventType.ServerStartedListening:
                    report += $"NOW ACCEPTING CONNECTIONS ON PORT {LocalPortNumber}{Environment.NewLine}";
                    break;

                case ServerEventType.ServerStoppedListening:
                    report += "SERVER SHUTDOWN COMPLETE, NO LONGER ACCEPTING CONNECTIONS";
                    break;

                case ServerEventType.ConnectionAccepted:
                    report += $"Connection accepted from {RemoteServerIpAddress}";
                    break;

                case ServerEventType.ConnectToRemoteServerStarted:
                    report +=
                        $"START PROCESS: CONNECT TO SERVER: {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case ServerEventType.ConnectToRemoteServerComplete:
                    report += $"PROCESS COMPLETE: CONNECT TO SERVER: {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case ServerEventType.ReceiveRequestFromRemoteServerStarted:
                    report += "START PROCESS: RECEIVE SERVER REQUEST";
                    break;

                case ServerEventType.ReceiveRequestFromRemoteServerComplete:
                    report += $"PROCESS COMPLETE: RECEIVE SERVER REQUEST{Environment.NewLine}";
                    break;

                case ServerEventType.ReceiveRequestLengthStarted:
                    report += "Step 1: Determine request length from first 4 bytes received";
                    break;

                case ServerEventType.ReceiveRequestLengthComplete:
                    report += 
                        $"First 4 bytes received: [{RequestLengthBytes.ToHexString()}] is " +
                        $"{RequestLengthInBytes:N0} encoded as Int32 " +
                        $"(Length of incoming request is {RequestLengthInBytes:N0} bytes)" +
                        Environment.NewLine;
                    break;

                case ServerEventType.ReceivedRequestLengthBytesFromSocket:
                    report +=
                        $"Received data from socket:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Received..: {BytesReceivedCount:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Expected Bytes..: {RequestLengthInBytes:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Unread Bytes....: {UnreadBytesCount:N0}{Environment.NewLine}";
                    break;

                case ServerEventType.SaveUnreadBytesAfterRequestLengthReceived:
                case ServerEventType.SaveUnreadBytesAfterAllRequestBytesReceived:
                    report +=
                        $"Socket.Receive operation returned {UnreadBytesCount:N0} more bytes than expected";
                    break;

                case ServerEventType.CopySavedBytesToRequestData:
                    report +=
                        $"Processed unread bytes as request data:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Unread Bytes.....: {UnreadBytesCount:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Request Length...: {RequestLengthInBytes:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Remaining..: {RequestBytesRemaining:N0}{Environment.NewLine}";
                    break;

                case ServerEventType.ReceivedRequestBytesFromSocket:
                    report +=
                        $"Received data from socket:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Socket Read Count........: {SocketReadCount:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Received...........: {BytesReceivedCount:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Request Bytes (Current)..: {CurrentRequestBytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Request Bytes (Total)....: {TotalRequestBytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Request Length...........: {RequestLengthInBytes:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Remaining..........: {RequestBytesRemaining:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Unread Bytes.............: {UnreadBytesCount:N0}{Environment.NewLine}";
                    break;

                case ServerEventType.ReceiveRequestBytesStarted:
                    report += "Step 2: Receive request bytes";
                    break;

                case ServerEventType.ReceiveRequestBytesComplete:
                    report += $"Successfully received all request bytes{Environment.NewLine}";
                    break;

                case ServerEventType.DetermineRequestTypeStarted:
                    report += "Step 3: Determine request type";
                    break;

                case ServerEventType.DetermineRequestTypeComplete:
                    report +=
                        $"Successfully determined type of incoming request: {RequestType.Name()}";
                    break;

                case ServerEventType.ProcessRequestStarted:
                    report += $"START PROCESS: {RequestType.Name()}";
                    break;

                case ServerEventType.ProcessRequestComplete:
                    report += $"PROCESS COMPLETE: {RequestType.Name()}{Environment.NewLine}";
                    break;

                case ServerEventType.QueueContainsUnhandledRequests:
                    report += $"Number of unhandled requests currently in queue: {ItemsInQueueCount}";
                    break;

                case ServerEventType.ShutdownListenSocketStarted:
                    report += "Attempting to shutdown listening socket...";
                    break;

                case ServerEventType.ShutdownListenSocketCompletedWithoutError:
                    report += "Successfully shutdown listening socket";
                    break;

                case ServerEventType.ShutdownListenSocketCompletedWithError:
                    report +=
                        $"Error occurred while attempting to shutdown listening socket: {ErrorMessage}";
                    break;

                case ServerEventType.SendTextMessageStarted:
                    report +=
                        $"Sending text message to {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Message: {TextMessage}{Environment.NewLine}";
                    break;

                case ServerEventType.SendTextMessageComplete:
                    report += $"Text message was successfully sent{Environment.NewLine}";
                    break;

                case ServerEventType.ReceivedTextMessage:
                    report +=
                        $"Text message received{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Message From..: {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}Message.......: {TextMessage}{Environment.NewLine}";
                    break;

                case ServerEventType.MarkTextMessageAsRead:
                    report += $"Text message from {RemoteServerIpAddress}:{RemoteServerPortNumber} has been marked as read";
                    break;

                case ServerEventType.RequestServerInfoStarted:
                    report += $"Sending request for additional server info to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case ServerEventType.RequestServerInfoComplete:
                case ServerEventType.RequestFileListComplete:
                case ServerEventType.RequestInboundFileTransferComplete:
                case ServerEventType.RequestOutboundFileTransferComplete:
                case ServerEventType.RetryOutboundFileTransferComplete:
                    report += $"Request was successfully sent{Environment.NewLine}";
                    break;

                case ServerEventType.ReceivedServerInfoRequest:
                    report += $"Received request for additional server info from: {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;
                    
                case ServerEventType.ReceivedRetryOutboundFileTransferRequest:
                    report +=
                        $"Received request to retry a failed outbound file transfer:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Send File To...: {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}File Name......: {FileName}{Environment.NewLine}" +
                        $"{indentLevel1}File Size......: {FileSizeInBytes:N0} bytes ({FileSizeString}){Environment.NewLine}" +
                        $"{indentLevel1}File Location..: {LocalFolder}{Environment.NewLine}" +
                        $"{indentLevel1}Target Folder..: {RemoteFolder}{Environment.NewLine}" +
                        $"{indentLevel1}Retry Counter..: {RetryCounter}{Environment.NewLine}" +
                        $"{indentLevel1}Retry Limit....: {RemoteServerRetryLimit}{Environment.NewLine}";
                    break;

                case ServerEventType.SendServerInfoStarted:
                    report += $"Sending additional server info to: {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +                             
                              $"{indentLevel1}Local IP.........: {LocalIpAddress}{Environment.NewLine}" +
                              $"{indentLevel1}Public IP........: {PublicIpAddress}{Environment.NewLine}" +
                              $"{indentLevel1}Port Number......: {LocalPortNumber}{Environment.NewLine}" +
                              $"{indentLevel1}Platform.........: {RemoteServerPlatform}{Environment.NewLine}" +
                              $"{indentLevel1}Transfer Folder..: {LocalFolder}{Environment.NewLine}";
                    break;

                case ServerEventType.SendServerInfoComplete:
                    report += $"Server info was successfully sent{Environment.NewLine}";
                    break;

                case ServerEventType.ReceivedServerInfo:
                    report +=
                        $"Received additional server info from: {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Session IP.......: {RemoteServerIpAddress}{Environment.NewLine}" +
                        $"{indentLevel1}Local IP.........: {LocalIpAddress}{Environment.NewLine}" +
                        $"{indentLevel1}Public IP........: {PublicIpAddress}{Environment.NewLine}" +
                        $"{indentLevel1}Port Number......: {RemoteServerPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}Platform.........: {RemoteServerPlatform}{Environment.NewLine}" +
                        $"{indentLevel1}Transfer Folder..: {RemoteFolder}{Environment.NewLine}";
                    break;

                case ServerEventType.RequestFileListStarted:
                    report +=
                        $"Sending request to {RemoteServerIpAddress}:{RemoteServerPortNumber} for list of files available to download{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Requested By...: {LocalIpAddress}:{LocalPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}Target Folder..: {RemoteFolder}{Environment.NewLine}";
                    break;

                case ServerEventType.ReceivedFileListRequest:
                    report += $"Received request for list of files available to download:{Environment.NewLine}{Environment.NewLine}" +
                              $"{indentLevel1}Send Response To..: {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}" +
                              $"{indentLevel1}Target Folder.....: {LocalFolder}{Environment.NewLine}";
                    break;

                case ServerEventType.SendFileListStarted:
                    report +=
                        $"Sending list of files available to download to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case ServerEventType.SendFileListComplete:
                    report += $"File list was successfully sent{Environment.NewLine}";
                    break;

                case ServerEventType.ReceivedFileList:
                    report +=
                        $"List of files available to download received from {RemoteServerIpAddress}:{RemoteServerPortNumber}, " +
                        $"{RemoteServerFileList.Count} files available{Environment.NewLine}";
                    break;

                case ServerEventType.RequestInboundFileTransferStarted:
                    report +=
                        $"Sending inbound file transfer request to {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Requested By...: {LocalIpAddress}:{LocalPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}File Name......: {FileName}{Environment.NewLine}" +
                        $"{indentLevel1}File Location..: {RemoteFolder}{Environment.NewLine}" +
                        $"{indentLevel1}Target Folder..: {LocalFolder}{Environment.NewLine}";
                    break;

                case ServerEventType.ReceivedInboundFileTransferRequest:
                    report +=
                        $"Received inbound file transfer request:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}File Sender....: {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}File Name......: {FileName}{Environment.NewLine}" +
                        $"{indentLevel1}File Size......: {FileSizeInBytes:N0} bytes ({FileSizeString}){Environment.NewLine}" +
                        $"{indentLevel1}Target Folder..: {LocalFolder}{Environment.NewLine}";
                    break;

                case ServerEventType.RequestOutboundFileTransferStarted:
                    report +=
                        $"Sending outbound file transfer request to {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}File Name......: {FileName}{Environment.NewLine}" +
                        $"{indentLevel1}File Size......: {FileSizeInBytes:N0} bytes ({FileSizeString}){Environment.NewLine}" +
                        $"{indentLevel1}File Location..: {LocalFolder}{Environment.NewLine}" +
                        $"{indentLevel1}Target Folder..: {RemoteFolder}{Environment.NewLine}";
                    break;

                case ServerEventType.ReceivedOutboundFileTransferRequest:
                    report +=
                        $"Received outbound file transfer request:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Send File To...: {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}File Name......: {FileName}{Environment.NewLine}" +
                        $"{indentLevel1}File Size......: {FileSizeInBytes:N0} bytes ({FileSizeString}){Environment.NewLine}" +
                        $"{indentLevel1}File Location..: {LocalFolder}{Environment.NewLine}" +
                        $"{indentLevel1}Target Folder..: {RemoteFolder}{Environment.NewLine}";
                    break;

                case ServerEventType.SendFileTransferAcceptedStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that file transfer has been accepted";
                    break;

                case ServerEventType.SendFileTransferAcceptedComplete:
                case ServerEventType.SendFileTransferRejectedComplete:
                case ServerEventType.SendFileTransferStalledComplete:
                case ServerEventType.SendFileTransferCompletedCompleted:
                case ServerEventType.SendRetryLimitExceededCompleted:
                case ServerEventType.SendNotificationNoFilesToDownloadComplete:
                case ServerEventType.SendNotificationFolderDoesNotExistComplete:
                case ServerEventType.SendNotificationFileDoesNotExistComplete:
                    report += $"Notification was successfully sent{Environment.NewLine}";
                    break;

                case ServerEventType.RemoteServerAcceptedFileTransfer:
                    report += $"File transfer accepted by {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}";
                    break;

                case ServerEventType.SendFileTransferRejectedStarted:
                    report +=
                        $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that the file transfer has been rejected";
                    break;

                case ServerEventType.RemoteServerRejectedFileTransfer:
                    report += $"File transfer rejected by {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case ServerEventType.SendFileBytesStarted:
                    report += $"Sending file to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case ServerEventType.SentFileChunkToClient:
                    report +=
                        $"Sent file chunk #{FileChunkSentCount:N0} ({SocketSendCount:N0} total Socket.Send calls):{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Current Bytes Sent..: {CurrentFileBytesSent:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Remaining.....: {FileBytesRemaining:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Size In Bytes..: {FileSizeInBytes:N0}{Environment.NewLine}";
                    break;

                case ServerEventType.SendFileBytesComplete:
                    report += $"Successfully sent file to {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}";
                    break;

                case ServerEventType.CopySavedBytesToIncomingFile:
                    report +=
                        $"Processed unread bytes as file data{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Saved Bytes......: {CurrentFileBytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Size........: {FileSizeInBytes:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Remaining..: {FileBytesRemaining:N0}{Environment.NewLine}";
                    break;

                case ServerEventType.ReceiveFileBytesStarted:
                    report += $"Receiving file from {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case ServerEventType.ReceivedFileBytesFromSocket:
                    report +=
                        $"Received Data From Socket:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Socket Read Count.....: {SocketReadCount:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Received........: {BytesReceivedCount:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Bytes (Current)..: {CurrentFileBytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Bytes (Total)....: {TotalFileBytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Size.............: {FileSizeInBytes:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Remaining.......: {FileBytesRemaining:N0}{Environment.NewLine}";
                    break;

                case ServerEventType.MultipleFileWriteAttemptsNeeded:
                    report +=
                        $"Last file chunk needed {FileWriteAttempts} attempts to write to disk successfully ({PercentComplete:P2} Complete)";
                    break;

                case ServerEventType.UpdateFileTransferProgress:
                    report += $"File Transfer Progress Update: {PercentComplete:P2} Complete";
                    break;

                case ServerEventType.ReceiveFileBytesComplete:
                    report +=
                        $"Successfully received file from {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Download Started...: {FileTransferStartTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                        $"{indentLevel1}Download Finished..: {FileTransferCompleteTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                        $"{indentLevel1}Elapsed Time.......: {FileTransferDurationTimeString}{Environment.NewLine}" +
                        $"{indentLevel1}Transfer Rate......: {FileTransferRate}{Environment.NewLine}";
                    break;

                case ServerEventType.SendFileTransferStalledStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that file transfer has stalled";
                    break;

                case ServerEventType.FileTransferStalled:
                    report += $"Received notification from {RemoteServerIpAddress}:{RemoteServerPortNumber} that file transfer is incomplete and data has stopped being received";
                    break;

                case ServerEventType.RetryOutboundFileTransferStarted:
                    report += $"Sending request to retry unsuccessful file transfer to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case ServerEventType.SendNotificationNoFilesToDownloadStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that no files are available to download from the requested folder";
                    break;

                case ServerEventType.ReceivedNotificationNoFilesToDownload:
                    report += $"Received notification from {RemoteServerIpAddress}:{RemoteServerPortNumber} that no files are available to download from the requested folder";
                    break;

                case ServerEventType.SendNotificationFolderDoesNotExistStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that the requested folder does not exist";
                    break;

                case ServerEventType.ReceivedNotificationFolderDoesNotExist:
                    report += $"Received notification from {RemoteServerIpAddress}:{RemoteServerPortNumber} that the requested folder does not exist";
                    break;

                case ServerEventType.SendShutdownServerCommandStarted:
                    report += "START PROCESS: INITIATE SERVER SHUTDOWN";
                    break;

                case ServerEventType.SendShutdownServerCommandComplete:
                    report += $"PROCESS COMPLETE: INITIATE SERVER SHUTDOWN{Environment.NewLine}";
                    break;

                case ServerEventType.ReceivedShutdownServerCommand:
                    report += $"Shutdown command was received from {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case ServerEventType.ErrorOccurred:
                    report += $"Error Occurred!{Environment.NewLine}{Environment.NewLine}" +
                              ErrorMessage + Environment.NewLine;
                    break;

                case ServerEventType.FileTransferStatusChange:
                    report += $"File transfer status is now {FileTransferStatusCurrent} (previous status was {FileTransferStatusPrevious})";
                    break;

                case ServerEventType.SendFileTransferCompletedStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that the file was received successfully";
                    break;

                case ServerEventType.RemoteServerConfirmedFileTransferCompleted:
                    report +=
                        $"{RemoteServerIpAddress}:{RemoteServerPortNumber} confirmed that " +
                        $"the file transfer was received successfully{Environment.NewLine}";
                    break;

                case ServerEventType.SendRetryLimitExceededStarted:
                    report +=
                        $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that \"{FileName}\" cannot be downloaded due to repeated failed attempts:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}File Name.................: {FileName}{Environment.NewLine}" +
                        $"{indentLevel1}Download Attempts.........: {RetryCounter}{Environment.NewLine}" +
                        $"{indentLevel1}Max Attempts Allowed......: {RemoteServerRetryLimit}{Environment.NewLine}" +
                        $"{indentLevel1}Download Lockout Expires..: {FileTransferCompleteTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                        $"{indentLevel1}Remaining Lockout Time....: {RetryLockoutTimeRemianing}{Environment.NewLine}";
                    break;

                case ServerEventType.ReceivedRetryLimitExceeded:
                    report +=
                        "Maximum # of attempts to complete stalled file transfer reached or exceeded: " +
                        $"{indentLevel1}File Name.................: {FileName}{Environment.NewLine}" +
                        $"{indentLevel1}Download Attempts.........: {RetryCounter}{Environment.NewLine}" +
                        $"{indentLevel1}Max Attempts Allowed......: {RemoteServerRetryLimit}{Environment.NewLine}" +
                        $"{indentLevel1}Download Lockout Expires..: {FileTransferCompleteTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                        $"{indentLevel1}Remaining Lockout Time....: {RetryLockoutTimeRemianing}{Environment.NewLine}";
                    break;

                case ServerEventType.SendNotificationFileDoesNotExistStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that the requested file does not exist";
                    break;

                case ServerEventType.ReceivedNotificationFileDoesNotExist:
                    report += $"Received notification from {RemoteServerIpAddress}:{RemoteServerPortNumber} that the requested file does not exist";
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            return report;
        }
    }
}
