using System;
using System.Net;
using AaronLuna.AsyncSocketServer.FileTransfers;
using AaronLuna.AsyncSocketServer.Requests;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.IO;
using AaronLuna.Common.Numeric;

namespace AaronLuna.AsyncSocketServer
{
    public class ServerEvent
    {
        public ServerEvent()
        {
            TimeStamp = DateTime.Now;
        }

        public DateTime TimeStamp { get; private set; }
        public EventType EventType { get; set; }

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
        public RequestType RequestType { get; set; }
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
        public Type SenderType { get; set; }

        public bool DoNotDisplayInLog => EventType.DoNotDisplayInLog();
        public bool LogLevelIsTraceOnly => EventType.LogLevelIsTraceOnly();
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
                case EventType.ServerStartedListening:
                    report += $"NOW ACCEPTING CONNECTIONS ON PORT {LocalPortNumber}{Environment.NewLine}";
                    break;

                case EventType.ServerStoppedListening:
                    report += "SERVER SHUTDOWN COMPLETE, NO LONGER ACCEPTING CONNECTIONS";
                    break;

                case EventType.ConnectionAccepted:
                    report += $"Accepted socket connection from {RemoteServerIpAddress}{Environment.NewLine}";
                    break;

                case EventType.ConnectToRemoteServerStarted:
                    report +=
                        $"START PROCESS: CONNECT TO SERVER: {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.ConnectToRemoteServerComplete:
                    report += $"PROCESS COMPLETE: CONNECT TO SERVER: {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.ReceiveRequestFromRemoteServerStarted:
                    report += "START PROCESS: RECEIVE SERVER REQUEST";
                    break;

                case EventType.ReceiveRequestFromRemoteServerComplete:
                    report += $"PROCESS COMPLETE: RECEIVE SERVER REQUEST{Environment.NewLine}";
                    break;

                case EventType.ReceiveRequestLengthStarted:
                    report += "Step 1: Determine request length from first 4 bytes received";
                    break;

                case EventType.ReceiveRequestLengthComplete:
                    report +=
                        $"First 4 bytes received: [{RequestLengthBytes.ToHexString()}] is " +
                        $"{RequestLengthInBytes:N0} encoded as Int32 " +
                        $"(Length of incoming request is {RequestLengthInBytes:N0} bytes)" +
                        Environment.NewLine;
                    break;

                case EventType.ReceivedRequestLengthBytesFromSocket:
                    report +=
                        $"Received data from socket:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Received..: {BytesReceivedCount:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Expected Bytes..: {RequestLengthInBytes:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Unread Bytes....: {UnreadBytesCount:N0}{Environment.NewLine}";
                    break;

                case EventType.SaveUnreadBytesAfterRequestLengthReceived:
                case EventType.SaveUnreadBytesAfterAllRequestBytesReceived:
                    report +=
                        $"Socket.Receive operation returned {UnreadBytesCount:N0} more bytes than expected";
                    break;

                case EventType.CopySavedBytesToRequestData:
                    report +=
                        $"Processed unread bytes as request data:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Unread Bytes.....: {UnreadBytesCount:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Request Length...: {RequestLengthInBytes:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Remaining..: {RequestBytesRemaining:N0}{Environment.NewLine}";
                    break;

                case EventType.ReceivedRequestBytesFromSocket:
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

                case EventType.ReceiveRequestBytesStarted:
                    report += "Step 2: Receive request bytes";
                    break;

                case EventType.ReceiveRequestBytesComplete:
                    report += $"Successfully received all request bytes{Environment.NewLine}";
                    break;

                case EventType.DetermineRequestTypeStarted:
                    report += "Step 3: Determine request type";
                    break;

                case EventType.DetermineRequestTypeComplete:
                    report +=
                        $"Successfully determined type of incoming request: {RequestType.Name()}{Environment.NewLine}";
                    break;

                case EventType.ProcessRequestStarted:
                    report += $"START PROCESS: {RequestType.Name()}";
                    break;

                case EventType.ProcessRequestComplete:
                    report += $"PROCESS COMPLETE: {RequestType.Name()}{Environment.NewLine}";
                    break;

                case EventType.PendingRequestInQueue:

                    var pendingRequestPlural = ItemsInQueueCount > 1
                        ? "requests are"
                        : "request is";

                    report += $"{ItemsInQueueCount} pending {pendingRequestPlural} waiting to be " +
                              $"processed{Environment.NewLine}";
                    break;

                case EventType.ProcessRequestBacklogStarted:

                    var requestStartPlural = ItemsInQueueCount > 1
                        ? "requests"
                        : "request";

                    report += $"Now processing {ItemsInQueueCount} {requestStartPlural} waiting in queue";
                    break;

                case EventType.ProcessRequestBacklogComplete:

                    var requesCompletetPlural = ItemsInQueueCount == 0 || ItemsInQueueCount > 1
                        ? "requests"
                        : "request";

                    report += $"Finished processing backlog of requests, queue now contains {ItemsInQueueCount} pending {requesCompletetPlural}";
                    break;

                case EventType.PendingFileTransfer:

                    var fileTransferPlural = ItemsInQueueCount > 1
                        ? "file transfers are"
                        : "file transfer is";

                    report += $"{ItemsInQueueCount} inbound {fileTransferPlural} waiting to be " +
                              $"processed{Environment.NewLine}";
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
                        $"{indentLevel1}Message: {TextMessage}{Environment.NewLine}";
                    break;

                case EventType.SendTextMessageComplete:
                    report += $"Text message was successfully sent{Environment.NewLine}";
                    break;

                case EventType.ReceivedTextMessage:
                    report +=
                        $"Text message received{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Message From..: {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}Message.......: {TextMessage}{Environment.NewLine}";
                    break;

                case EventType.MarkTextMessageAsRead:
                    report += $"Text message from {RemoteServerIpAddress}:{RemoteServerPortNumber} has been marked as read";
                    break;

                case EventType.RequestServerInfoStarted:
                    report += $"Sending request for additional server info to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.RequestServerInfoComplete:
                case EventType.RequestFileListComplete:
                case EventType.RequestInboundFileTransferComplete:
                case EventType.RequestOutboundFileTransferComplete:
                case EventType.RetryOutboundFileTransferComplete:
                    report += $"Request was successfully sent{Environment.NewLine}";
                    break;

                case EventType.ReceivedServerInfoRequest:
                    report += $"Received request for additional server info from: {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.ReceivedRetryOutboundFileTransferRequest:
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

                case EventType.SendServerInfoStarted:
                    report += $"Sending additional server info to: {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +
                              $"{indentLevel1}Local IP.........: {LocalIpAddress}{Environment.NewLine}" +
                              $"{indentLevel1}Public IP........: {PublicIpAddress}{Environment.NewLine}" +
                              $"{indentLevel1}Port Number......: {LocalPortNumber}{Environment.NewLine}" +
                              $"{indentLevel1}Platform.........: {RemoteServerPlatform}{Environment.NewLine}" +
                              $"{indentLevel1}Transfer Folder..: {LocalFolder}{Environment.NewLine}";
                    break;

                case EventType.SendServerInfoComplete:
                    report += $"Server info was successfully sent{Environment.NewLine}";
                    break;

                case EventType.ReceivedServerInfo:
                    report +=
                        $"Received additional server info from: {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Session IP.......: {RemoteServerIpAddress}{Environment.NewLine}" +
                        $"{indentLevel1}Local IP.........: {LocalIpAddress}{Environment.NewLine}" +
                        $"{indentLevel1}Public IP........: {PublicIpAddress}{Environment.NewLine}" +
                        $"{indentLevel1}Port Number......: {RemoteServerPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}Platform.........: {RemoteServerPlatform}{Environment.NewLine}" +
                        $"{indentLevel1}Transfer Folder..: {RemoteFolder}{Environment.NewLine}";
                    break;

                case EventType.RequestFileListStarted:
                    report +=
                        $"Sending request to {RemoteServerIpAddress}:{RemoteServerPortNumber} for list of files available to download{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Requested By...: {LocalIpAddress}:{LocalPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}Target Folder..: {RemoteFolder}{Environment.NewLine}";
                    break;

                case EventType.ReceivedFileListRequest:
                    report += $"Received request for list of files available to download:{Environment.NewLine}{Environment.NewLine}" +
                              $"{indentLevel1}Send Response To..: {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}" +
                              $"{indentLevel1}Target Folder.....: {LocalFolder}{Environment.NewLine}";
                    break;

                case EventType.SendFileListStarted:
                    report +=
                        $"Sending list of files available to download to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.SendFileListComplete:
                    report += $"File list was successfully sent{Environment.NewLine}";
                    break;

                case EventType.ReceivedFileList:
                    report +=
                        $"List of files available to download received from {RemoteServerIpAddress}:{RemoteServerPortNumber}, " +
                        $"{RemoteServerFileList.Count} files available{Environment.NewLine}";
                    break;

                case EventType.RequestInboundFileTransferStarted:
                    report +=
                        $"Sending inbound file transfer request to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.ReceivedInboundFileTransferRequest:
                    report +=
                        $"Received inbound file transfer request:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}File Sender....: {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}File Name......: {FileName}{Environment.NewLine}" +
                        $"{indentLevel1}File Size......: {FileSizeInBytes:N0} bytes ({FileSizeString}){Environment.NewLine}" +
                        $"{indentLevel1}Target Folder..: {LocalFolder}{Environment.NewLine}";
                    break;

                case EventType.RequestOutboundFileTransferStarted:
                    report +=
                        $"Sending outbound file transfer request to {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}File Name......: {FileName}{Environment.NewLine}" +
                        $"{indentLevel1}File Size......: {FileSizeInBytes:N0} bytes ({FileSizeString}){Environment.NewLine}" +
                        $"{indentLevel1}File Location..: {LocalFolder}{Environment.NewLine}" +
                        $"{indentLevel1}Target Folder..: {RemoteFolder}{Environment.NewLine}";
                    break;

                case EventType.ReceivedOutboundFileTransferRequest:
                    report +=
                        $"Received outbound file transfer request:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Send File To...: {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}" +
                        $"{indentLevel1}File Name......: {FileName}{Environment.NewLine}" +
                        $"{indentLevel1}File Location..: {LocalFolder}{Environment.NewLine}" +
                        $"{indentLevel1}Target Folder..: {RemoteFolder}{Environment.NewLine}";
                    break;

                case EventType.SendFileTransferAcceptedStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that file transfer has been accepted";
                    break;

                case EventType.SendFileTransferAcceptedComplete:
                case EventType.SendFileTransferRejectedComplete:
                case EventType.SendFileTransferStalledComplete:
                case EventType.SendFileTransferCompletedComplete:
                case EventType.SendRetryLimitExceededCompleted:
                case EventType.SendNotificationFolderIsEmptyComplete:
                case EventType.SendNotificationFolderDoesNotExistComplete:
                case EventType.SendNotificationFileDoesNotExistComplete:
                    report += $"Notification was successfully sent{Environment.NewLine}";
                    break;

                case EventType.RemoteServerAcceptedFileTransfer:
                    report += $"File transfer accepted by {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}";
                    break;

                case EventType.SendFileTransferRejectedStarted:
                    report +=
                        $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that the file transfer has been rejected";
                    break;

                case EventType.RemoteServerRejectedFileTransfer:
                    report += $"File transfer rejected by {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}";
                    break;

                case EventType.SendFileBytesStarted:
                    report += $"Sending file to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.SentFileChunkToRemoteServer:
                    report +=
                        $"Sent file chunk #{FileChunkSentCount:N0} ({SocketSendCount:N0} total Socket.Send calls):{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Current Bytes Sent..: {CurrentFileBytesSent:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Remaining.....: {FileBytesRemaining:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Size In Bytes..: {FileSizeInBytes:N0}{Environment.NewLine}";
                    break;

                case EventType.SendFileBytesComplete:
                    report += $"Successfully sent file to {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}";
                    break;

                case EventType.CopySavedBytesToIncomingFile:
                    report +=
                        $"Processed unread bytes as file data{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Saved Bytes......: {CurrentFileBytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Size........: {FileSizeInBytes:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Remaining..: {FileBytesRemaining:N0}{Environment.NewLine}";
                    break;

                case EventType.ReceiveFileBytesStarted:
                    report += $"Receiving file from {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.ReceivedFileBytesFromSocket:
                    report +=
                        $"Received Data From Socket:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Socket Read Count.....: {SocketReadCount:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Received........: {BytesReceivedCount:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Bytes (Current)..: {CurrentFileBytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Bytes (Total)....: {TotalFileBytesReceived:N0}{Environment.NewLine}" +
                        $"{indentLevel1}File Size.............: {FileSizeInBytes:N0}{Environment.NewLine}" +
                        $"{indentLevel1}Bytes Remaining.......: {FileBytesRemaining:N0}{Environment.NewLine}";
                    break;

                case EventType.MultipleFileWriteAttemptsNeeded:
                    report +=
                        $"Last file chunk needed {FileWriteAttempts} attempts to write to disk successfully ({PercentComplete:P2} Complete)";
                    break;

                case EventType.UpdateFileTransferProgress:
                    report += $"File Transfer Progress Update: {PercentComplete:P2} Complete";
                    break;

                case EventType.ReceiveFileBytesComplete:
                    report +=
                        $"Successfully received file from {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}Download Started...: {FileTransferStartTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                        $"{indentLevel1}Download Finished..: {FileTransferCompleteTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                        $"{indentLevel1}Elapsed Time.......: {FileTransferDurationTimeString}{Environment.NewLine}" +
                        $"{indentLevel1}Transfer Rate......: {FileTransferRate}{Environment.NewLine}";
                    break;

                case EventType.SendFileTransferStalledStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that file transfer has stalled";
                    break;

                case EventType.FileTransferStalled:
                    report += $"Received notification from {RemoteServerIpAddress}:{RemoteServerPortNumber} that file transfer is incomplete and data has stopped being received{Environment.NewLine}";
                    break;

                case EventType.RetryOutboundFileTransferStarted:
                    report += $"Sending request to retry unsuccessful file transfer to {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.SendNotificationFolderIsEmptyStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that the requested folder is empty";
                    break;

                case EventType.ReceivedNotificationFolderIsEmpty:
                    report += $"Received notification from {RemoteServerIpAddress}:{RemoteServerPortNumber} that the requested folder is empty";
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
                    report += $"PROCESS COMPLETE: INITIATE SERVER SHUTDOWN{Environment.NewLine}";
                    break;

                case EventType.ReceivedShutdownServerCommand:
                    report += $"Shutdown command was received from {RemoteServerIpAddress}:{RemoteServerPortNumber}";
                    break;

                case EventType.ErrorOccurred:
                    report += $"Error Occurred!{Environment.NewLine}{Environment.NewLine}" +
                              ErrorMessage + Environment.NewLine;
                    break;

                case EventType.FileTransferStatusChange:
                    report += $"File transfer status is now {FileTransferStatusCurrent} (previous status was {FileTransferStatusPrevious})";
                    break;

                case EventType.SendFileTransferCompletedStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that the file was received successfully";
                    break;

                case EventType.RemoteServerConfirmedFileTransferCompleted:
                    report +=
                        $"{RemoteServerIpAddress}:{RemoteServerPortNumber} confirmed that " +
                        $"the file transfer was received successfully{Environment.NewLine}";
                    break;

                case EventType.SendRetryLimitExceededStarted:
                    report +=
                        $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that \"{FileName}\" cannot be downloaded due to repeated failed attempts:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}File Name.................: {FileName}{Environment.NewLine}" +
                        $"{indentLevel1}Download Attempts.........: {RetryCounter}{Environment.NewLine}" +
                        $"{indentLevel1}Max Attempts Allowed......: {RemoteServerRetryLimit}{Environment.NewLine}" +
                        $"{indentLevel1}Download Lockout Expires..: {RetryLockoutExpireTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                        $"{indentLevel1}Remaining Lockout Time....: {RetryLockoutTimeRemianing}{Environment.NewLine}";
                    break;

                case EventType.ReceivedRetryLimitExceeded:
                    report +=
                        $"Maximum # of attempts to complete stalled file transfer reached or exceeded: {Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}File Name.................: {FileName}{Environment.NewLine}" +
                        $"{indentLevel1}Download Attempts.........: {RetryCounter}{Environment.NewLine}" +
                        $"{indentLevel1}Max Attempts Allowed......: {RemoteServerRetryLimit}{Environment.NewLine}" +
                        $"{indentLevel1}Current Time..............: {DateTime.Now:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                        $"{indentLevel1}Download Lockout Expires..: {RetryLockoutExpireTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                        $"{indentLevel1}Remaining Lockout Time....: {RetryLockoutTimeRemianing}{Environment.NewLine}";
                    break;

                case EventType.RetryLimitLockoutExpired:
                    report +=
                        "The lockout period for exceeding the maximum number of download attempts has expired " +
                        $"for the file below:{Environment.NewLine}{Environment.NewLine}" +
                        $"{indentLevel1}File Name.................: {FileName}{Environment.NewLine}" +
                        $"{indentLevel1}Current Time..............: {DateTime.Now:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                        $"{indentLevel1}Download Lockout Expires..: {RetryLockoutExpireTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}";

                    break;

                case EventType.SendNotificationFileDoesNotExistStarted:
                    report += $"Notifying {RemoteServerIpAddress}:{RemoteServerPortNumber} that the requested file does not exist";
                    break;

                case EventType.ReceivedNotificationFileDoesNotExist:
                    report += $"Received notification from {RemoteServerIpAddress}:{RemoteServerPortNumber} that the requested file does not exist";
                    break;

                case EventType.StoppedSendingFileBytes:
                    report += $"No longer sending file bytes to remote server, file transfer is incomplete{Environment.NewLine}";
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            return report;
        }
    }
}
