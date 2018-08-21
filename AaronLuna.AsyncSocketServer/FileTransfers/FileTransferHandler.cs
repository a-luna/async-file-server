using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.Requests.RequestTypes;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.FileTransfers
{
    public class FileTransferHandler
    {
        int _fileTransferId;
        static readonly object LockAllFileTransfers = new object();

        public FileTransferHandler(ServerSettings settings)
        {
            _fileTransferId = 1;
            Settings = settings;

            FileReceiver = new FileReceiver(Settings);
            FileReceiver.EventOccurred += HandleServerEventOccurred;
            FileReceiver.SocketEventOccurred += HandleSocketEventOccurred;
            FileReceiver.FileTransferProgress += HandleFileTransferProgress;

            FileSender = new FileSender(Settings);
            FileSender.EventOccurred += HandleServerEventOccurred;
            FileSender.SocketEventOccurred += HandleSocketEventOccurred;
            FileSender.FileTransferProgress += HandleFileTransferProgress;

            PendingFileTransfers = new List<FileTransfer>();
            InProgressFileTransfers = new List<FileTransfer>();
            ReceivedFileTransfers = new List<FileTransfer>();
            SentFileTransfers = new List<FileTransfer>();
            RejectedFileTransfers = new List<FileTransfer>();
            StalledFileTransfers = new List<FileTransfer>();
            FailedFileTransfers = new List<FileTransfer>();
        }

        protected readonly ServerSettings Settings;
        protected FileReceiver FileReceiver;
        protected FileSender FileSender;
        protected readonly List<FileTransfer> PendingFileTransfers;
        protected readonly List<FileTransfer> InProgressFileTransfers;
        protected readonly List<FileTransfer> ReceivedFileTransfers;
        protected readonly List<FileTransfer> SentFileTransfers;
        protected readonly List<FileTransfer> RejectedFileTransfers;
        protected readonly List<FileTransfer> StalledFileTransfers;
        protected readonly List<FileTransfer> FailedFileTransfers;

        public List<FileTransfer> FileTransfers => DeepCopyAllFileTransfers();

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;
        public event EventHandler<ServerEvent> FileTransferProgress;
        public event EventHandler<string> ErrorOccurred;

        public event EventHandler<int> PendingFileTransfer;
        public event EventHandler<SendFileRequest> InboundFileAlreadyExists;
        public event EventHandler<FileTransfer> InboundFileTransferComplete;
        public event EventHandler<GetFileRequest> RequestedFileDoesNotExist;
        public event EventHandler<FileTransfer> ReceivedRetryOutboundFileTransferRequest;
        public event EventHandler<FileTransfer> RetryLimitLockoutExpired;

        public override string ToString()
        {
            var totalTransfers =
                PendingFileTransfers.Count + InProgressFileTransfers.Count +
                ReceivedFileTransfers.Count + SentFileTransfers.Count +
                RejectedFileTransfers.Count + StalledFileTransfers.Count +
                FailedFileTransfers.Count;

            var pending = PendingFileTransfers.Count > 0
                ? $", {PendingFileTransfers.Count} Pending"
                : string.Empty;

            var inProgress = InProgressFileTransfers.Count > 0
                ? $", {InProgressFileTransfers.Count} In Progress"
                : string.Empty;

            var rejected = RejectedFileTransfers.Count > 0
                ? $", {RejectedFileTransfers.Count} Rejected"
                : string.Empty;

            var stalled = StalledFileTransfers.Count > 0
                ? $", {StalledFileTransfers.Count} Stalled"
                : string.Empty;

            var errors = FailedFileTransfers.Count > 0
                ? $", {FailedFileTransfers.Count} Failed"
                : string.Empty;

            var details = totalTransfers > 0
                ? $" ({ReceivedFileTransfers.Count} Rx, {SentFileTransfers.Count} Tx" +
                  $"{pending}{inProgress}{rejected}{stalled}{errors})"
                : string.Empty;

            return $"[{totalTransfers} File Transfers{details}] ";
        }

        public FileTransfer InitializeFileTransfer(
            TransferDirection direction,
            FileTransferInitiator initiator,
            ServerInfo remoteServerInfo,
            string fileName,
            long fileSizeInBytes,
            string localFolderPath,
            string remoteFolderPath,
            long transferResponseCode,
            int remoteServerRetryLimit,
            int remoteServerTransferId)
        {
            var fileTransfer = new FileTransfer
            {
                Status = FileTransferStatus.Pending,
                RequestInitiatedTime = DateTime.Now,
                TransferDirection = direction,
                Initiator = initiator,
                RemoteServerInfo = remoteServerInfo,
                FileName = fileName,
                FileSizeInBytes = fileSizeInBytes,
                LocalFolderPath = localFolderPath,
                RemoteFolderPath = remoteFolderPath,
                TransferResponseCode = transferResponseCode,
                RemoteServerRetryLimit = remoteServerRetryLimit,
                RemoteServerTransferId = remoteServerTransferId
            };

            AssignNewFileTransferId(fileTransfer);

            return fileTransfer;
        }

        public Result HandleInboundFileTransferRequest(SendFileRequest sendFileRequest)
        {
            var getFileTransfer = GetInboundFileTransfer(sendFileRequest);
            if (getFileTransfer.Failure)
            {
                return Result.Fail(getFileTransfer.Error);
            }

            var inboundFileTransfer = getFileTransfer.Value;
            if (!File.Exists(inboundFileTransfer.LocalFilePath))
            {
                PendingFileTransfer?.Invoke(this, PendingFileTransfers.Count);
                return Result.Ok();
            }

            var error =
                $"The request below was rejected because \"{inboundFileTransfer.FileName}\" " +
                "already exists at the specified location:" +
                Environment.NewLine + Environment.NewLine + inboundFileTransfer.InboundRequestDetails(false);

            inboundFileTransfer.Status = FileTransferStatus.Rejected;
            inboundFileTransfer.ErrorMessage = error;

            RejectPendingFileTransfer(inboundFileTransfer);
            ReportError(error);

            InboundFileAlreadyExists?.Invoke(this, sendFileRequest);

            return Result.Fail(error);
        }

        public virtual async Task<Result> AcceptInboundFileTransferAsync(
            FileTransfer inboundFileTransfer,
            Socket socket,
            byte[] fileBytes,
            CancellationToken token)
        {
            inboundFileTransfer.Status = FileTransferStatus.Accepted;
            StartProcessingPendingFileTransfer(inboundFileTransfer);

            var receiveFile = await
                FileReceiver.ReceiveFileAsync(
                    inboundFileTransfer,
                    socket,
                    fileBytes,
                    token);

            if (receiveFile.Failure)
            {
                inboundFileTransfer.Status = FileTransferStatus.Error;
                inboundFileTransfer.ErrorMessage = receiveFile.Error;

                InProgressFileTransferHasFailed(inboundFileTransfer);
                ReportError(receiveFile.Error);

                return receiveFile;
            }

            if (inboundFileTransfer.Status == FileTransferStatus.Stalled)
            {
                FileTransferHasStalled(inboundFileTransfer);
                return Result.Ok();
            }

            SuccessfullyReceivedFileTransfer(inboundFileTransfer);
            InboundFileTransferComplete?.Invoke(this, inboundFileTransfer);

            return Result.Ok();
        }

        public void RejectInboundFileTransfer(FileTransfer fileTransfer)
        {
            fileTransfer.Status = FileTransferStatus.Rejected;
            fileTransfer.ErrorMessage = "File transfer was rejected by user";

            RejectPendingFileTransfer(fileTransfer);
        }

        public Result<FileTransfer> HandleInboundFileTransferStalled(int fileTransferId)
        {
            var getFileTransfer = GetFileTransferById(fileTransferId);
            if (getFileTransfer.Failure)
            {
                ReportError(getFileTransfer.Error);
                return Result.Fail<FileTransfer>(getFileTransfer.Error);
            }

            var fileTransfer = getFileTransfer.Value;
            fileTransfer.Status = FileTransferStatus.Stalled;
            fileTransfer.InboundFileTransferStalled = true;

            fileTransfer.ErrorMessage =
                "Data is no longer bring received from remote client, file transfer has been canceled " +
                "(SendNotificationFileTransferStalledAsync)";

            FileTransferHasStalled(fileTransfer);
            ReportError(fileTransfer.ErrorMessage);

            return Result.Ok(fileTransfer);
        }

        public Result<FileTransfer> RetryStalledInboundFileTransfer(int fileTransferId)
        {
            var getFileTransfer = GetFileTransferById(fileTransferId);
            if (getFileTransfer.Failure)
            {
                ReportError(getFileTransfer.Error);
                return Result.Fail<FileTransfer>(getFileTransfer.Error);
            }

            var stalledFileTransfer = getFileTransfer.Value;

            if (stalledFileTransfer.Status == FileTransferStatus.RetryLimitExceeded)
            {
                if (stalledFileTransfer.RetryLockoutExpired)
                {
                    stalledFileTransfer.ResetTransferValues();
                    stalledFileTransfer.RetryCounter = 1;
                    RetryLimitLockoutExpired?.Invoke(this, stalledFileTransfer);
                }
                else
                {
                    var retryLimitExceeded =
                        $"Maximum # of attempts to complete stalled file transfer reached or exceeded: {Environment.NewLine}" +
                        $"File Name.................: {stalledFileTransfer.FileName}{Environment.NewLine}" +
                        $"Download Attempts.........: {stalledFileTransfer.RetryCounter}{Environment.NewLine}" +
                        $"Max Attempts Allowed......: {stalledFileTransfer.RemoteServerRetryLimit}{Environment.NewLine}" +
                        $"Current Time..............: {DateTime.Now:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                        $"Download Lockout Expires..: {stalledFileTransfer.RetryLockoutExpireTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                        $"Remaining Lockout Time....: {stalledFileTransfer.RetryLockoutTimeRemianing}{Environment.NewLine}";

                    ReportError(retryLimitExceeded);
                    return Result.Fail<FileTransfer>(retryLimitExceeded);
                }
            }

            RetryStalledFileTransfer(stalledFileTransfer);

            return Result.Ok(stalledFileTransfer);
        }

        public Result<FileTransfer> HandleRetryLimitExceeded(RetryLimitExceeded retryLimitExceeded)
        {
            var getFileTransfer = GetFileTransferById(retryLimitExceeded.RemoteServerTransferId);
            if (getFileTransfer.Failure)
            {
                ReportError(getFileTransfer.Error);
                return Result.Fail<FileTransfer>(getFileTransfer.Error);
            }

            var inboundFileTransfer = getFileTransfer.Value;
            inboundFileTransfer.Status = FileTransferStatus.RetryLimitExceeded;
            inboundFileTransfer.RetryLockoutExpireTime = retryLimitExceeded.LockoutExpireTime;

            var retryLimitErrorMessage =
                $"Maximum # of attempts to complete stalled file transfer reached or exceeded: {Environment.NewLine}" +
                $"File Name.................: {inboundFileTransfer.FileName}{Environment.NewLine}" +
                $"Download Attempts.........: {inboundFileTransfer.RetryCounter}{Environment.NewLine}" +
                $"Max Attempts Allowed......: {inboundFileTransfer.RemoteServerRetryLimit}{Environment.NewLine}" +
                $"Current Time..............: {DateTime.Now:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                $"Download Lockout Expires..: {inboundFileTransfer.RetryLockoutExpireTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                $"Remaining Lockout Time....: {inboundFileTransfer.RetryLockoutTimeRemianing}{Environment.NewLine}";

            ReportError(retryLimitErrorMessage);

            return Result.Ok(inboundFileTransfer);
        }

        public Result AbortInboundFileTransfer(FileTransferResponse fileTransferResponse, string error)
        {
            var getFileTransfer = GetFileTransferById(fileTransferResponse.RemoteServerTransferId);
            if (getFileTransfer.Failure)
            {
                ReportError(getFileTransfer.Error);
                return getFileTransfer;
            }

            var fileTransfer = getFileTransfer.Value;
            fileTransfer.Status = FileTransferStatus.Error;
            fileTransfer.ErrorMessage = error;

            PendingFileTransferHasFailed(fileTransfer);
            ReportError(error);

            return Result.Ok();
        }

        public Result<FileTransfer> HandleOutboundFileTransferRequest(GetFileRequest getFileRequest)
        {
            var requestedFilePath = Path.Combine(getFileRequest.LocalFolderPath, getFileRequest.FileName);

            if (!File.Exists(requestedFilePath))
            {
                ReportError("Requested file does not exist");
                RequestedFileDoesNotExist?.Invoke(this, getFileRequest);
                return Result.Fail<FileTransfer>("Requested file does not exist");
            }

            var fileSizeInBytes = new FileInfo(requestedFilePath).Length;

            var outboundFileTransfer =
                InitializeFileTransfer(
                    TransferDirection.Outbound,
                    FileTransferInitiator.RemoteServer,
                    getFileRequest.RemoteServerInfo,
                    getFileRequest.FileName,
                    fileSizeInBytes,
                    getFileRequest.LocalFolderPath,
                    getFileRequest.RemoteFolderPath,
                    DateTime.Now.Ticks,
                    Settings.TransferRetryLimit,
                    getFileRequest.RemoteServerTransferId);

            return Result.Ok(outboundFileTransfer);
        }

        public Result HandleRequestedFileDoesNotExist(int fileTransferId)
        {
            var getFileTransfer = GetFileTransferById(fileTransferId);
            if (getFileTransfer.Failure)
            {
                ReportError(getFileTransfer.Error);
                return getFileTransfer;
            }

            var fileTransfer = getFileTransfer.Value;

            var error =
                $"Remote server rejected the request below because \"{fileTransfer.FileName}\" " +
                "does not exist at the specified location:" +
                Environment.NewLine + Environment.NewLine + fileTransfer.InboundRequestDetails(false);

            fileTransfer.Status = FileTransferStatus.Rejected;
            fileTransfer.ErrorMessage = error;

            RejectPendingFileTransfer(fileTransfer);
            ReportError(error);

            return Result.Ok();
        }

        public virtual async Task<Result> HandleOutboundFileTransferAccepted(
            FileTransferResponse fileTransferResponse,
            Socket socket,
            CancellationToken token)
        {
            var getFileTransfer = GetFileTransferByResponseCode(fileTransferResponse);
            if (getFileTransfer.Failure)
            {
                ReportError(getFileTransfer.Error);
                return getFileTransfer;
            }

            var outboundFileTransfer = getFileTransfer.Value;
            outboundFileTransfer.Status = FileTransferStatus.Accepted;

            StartProcessingPendingFileTransfer(outboundFileTransfer);

            if (socket == null)
            {
                var error = "Unable to retrieve transfer socket, file transfer must be aborted";
                outboundFileTransfer.Status = FileTransferStatus.Error;
                outboundFileTransfer.ErrorMessage = error;

                InProgressFileTransferHasFailed(outboundFileTransfer);
                ReportError(error);
                return Result.Fail(error);
            }

            var sendFile = await FileSender.SendFileAsync(outboundFileTransfer, socket, token);
            if (sendFile.Success) return Result.Ok();

            outboundFileTransfer.Status = FileTransferStatus.Error;
            outboundFileTransfer.ErrorMessage = sendFile.Error;

            InProgressFileTransferHasFailed(outboundFileTransfer);
            ReportError(sendFile.Error);

            return sendFile;
        }

        public Result HandleOutboundFileTransferRejected(FileTransferResponse fileTransferResponse)
        {
            var getFileTransfer = GetFileTransferByResponseCode(fileTransferResponse);
            if (getFileTransfer.Failure)
            {
                ReportError(getFileTransfer.Error);
                return getFileTransfer;
            }

            var outboundFileTransfer = getFileTransfer.Value;
            outboundFileTransfer.Status = FileTransferStatus.Rejected;
            outboundFileTransfer.ErrorMessage = "File transfer was rejected by remote server";

            RejectPendingFileTransfer(outboundFileTransfer);

            var error =
                "File transfer was rejected by remote server:" +
                Environment.NewLine + Environment.NewLine + outboundFileTransfer.OutboundRequestDetails();

            ReportError(error);

            return Result.Ok();
        }

        public Result HandleOutboundFileTransferComplete(FileTransferResponse fileTransferResponse)
        {
            var getFileTransfer = GetFileTransferByResponseCode(fileTransferResponse);
            if (getFileTransfer.Failure)
            {
                ReportError(getFileTransfer.Error);
                return getFileTransfer;
            }

            var outboundFileTransfer = getFileTransfer.Value;
            outboundFileTransfer.Status = FileTransferStatus.ConfirmedComplete;

            SuccessfullySentFileTransfer(outboundFileTransfer);

            return Result.Ok();
        }

        public Result HandleOutboundFileTransferStalled(FileTransferResponse fileTransferResponse)
        {
            const string fileTransferStalledErrorMessage =
                "Aborting file transfer, client says that data is no longer being received (HandleStalledFileTransfer)";

            var getFileTransfer = GetFileTransferByResponseCode(fileTransferResponse);
            if (getFileTransfer.Failure)
            {
                ReportError(getFileTransfer.Error);
                return getFileTransfer;
            }

            var outboundFileTransfer = getFileTransfer.Value;
            outboundFileTransfer.Status = FileTransferStatus.Cancelled;
            outboundFileTransfer.ErrorMessage = fileTransferStalledErrorMessage;
            outboundFileTransfer.OutboundFileTransferStalled = true;

            FileTransferHasStalled(outboundFileTransfer);
            ReportError(fileTransferStalledErrorMessage);

            return Result.Ok();
        }

        public void HandleRetryOutboundFileTransfer(FileTransferResponse fileTransferResponse)
        {
            var getFileTransfer = GetFileTransferByResponseCode(fileTransferResponse);
            if (getFileTransfer.Failure)
            {
                ReportError(getFileTransfer.Error);
                return;
            }

            var canceledFileTransfer = getFileTransfer.Value;

            RetryStalledFileTransfer(canceledFileTransfer);

            ReceivedRetryOutboundFileTransferRequest?.Invoke(this, canceledFileTransfer);
        }

        public void AbortOutboundFileTransfer(FileTransferResponse fileTransferResponse, string error)
        {
            var getFileTransfer = GetFileTransferByResponseCode(fileTransferResponse);
            if (getFileTransfer.Failure)
            {
                ReportError(getFileTransfer.Error);
                return;
            }

            var fileTransfer = getFileTransfer.Value;
            fileTransfer.Status = FileTransferStatus.Error;
            fileTransfer.ErrorMessage = error;

            PendingFileTransferHasFailed(fileTransfer);
        }

        void AssignNewFileTransferId(FileTransfer fileTransfer)
        {
            lock (LockAllFileTransfers)
            {
                fileTransfer.SetId(_fileTransferId);
                _fileTransferId++;

                PendingFileTransfers.Add(fileTransfer);
            }
        }

        void StartProcessingPendingFileTransfer(FileTransfer fileTransfer)
        {
            lock (LockAllFileTransfers)
            {
                var inProgressMatches = InProgressFileTransfers.Select(ft => ft.Id == fileTransfer.Id).Count();
                if (inProgressMatches > 0) return;

                PendingFileTransfers.RemoveAll(f => f.Id == fileTransfer.Id);
                InProgressFileTransfers.Add(fileTransfer);
            }
        }

        void SuccessfullyReceivedFileTransfer(FileTransfer fileTransfer)
        {
            lock (LockAllFileTransfers)
            {
                var receivedMatches = ReceivedFileTransfers.Select(ft => ft.Id == fileTransfer.Id).Count();
                if (receivedMatches > 0) return;

                InProgressFileTransfers.RemoveAll(f => f.Id == fileTransfer.Id);
                ReceivedFileTransfers.Add(fileTransfer);
            }
        }

        void SuccessfullySentFileTransfer(FileTransfer fileTransfer)
        {
            lock (LockAllFileTransfers)
            {
                var sentMatches = SentFileTransfers.Select(ft => ft.Id == fileTransfer.Id).Count();
                if (sentMatches > 0) return;

                InProgressFileTransfers.RemoveAll(f => f.Id == fileTransfer.Id);
                SentFileTransfers.Add(fileTransfer);
            }
        }

        void RejectPendingFileTransfer(FileTransfer fileTransfer)
        {
            lock (LockAllFileTransfers)
            {
                var rejectedMatches = RejectedFileTransfers.Select(ft => ft.Id == fileTransfer.Id).Count();
                if (rejectedMatches > 0) return;

                PendingFileTransfers.RemoveAll(f => f.Id == fileTransfer.Id);
                RejectedFileTransfers.Add(fileTransfer);
            }
        }

        void FileTransferHasStalled(FileTransfer fileTransfer)
        {
            lock (LockAllFileTransfers)
            {
                var stalledMatches = StalledFileTransfers.Select(ft => ft.Id == fileTransfer.Id).Count();
                if (stalledMatches > 0) return;

                InProgressFileTransfers.RemoveAll(f => f.Id == fileTransfer.Id);
                StalledFileTransfers.Add(fileTransfer);
            }
        }

        void RetryStalledFileTransfer(FileTransfer fileTransfer)
        {
            lock (LockAllFileTransfers)
            {
                var stalledMatches = StalledFileTransfers.Select(ft => ft.Id == fileTransfer.Id).Count();
                if (stalledMatches == 0) return;

                StalledFileTransfers.RemoveAll(f => f.Id == fileTransfer.Id);
                PendingFileTransfers.Add(fileTransfer);
            }
        }

        void PendingFileTransferHasFailed(FileTransfer fileTransfer)
        {
            lock (LockAllFileTransfers)
            {
                var failedMatches = FailedFileTransfers.Select(ft => ft.Id == fileTransfer.Id).Count();
                if (failedMatches > 0) return;

                PendingFileTransfers.RemoveAll(f => f.Id == fileTransfer.Id);
                FailedFileTransfers.Add(fileTransfer);
            }
        }

        void InProgressFileTransferHasFailed(FileTransfer fileTransfer)
        {
            lock (LockAllFileTransfers)
            {
                var failedMatches = FailedFileTransfers.Select(ft => ft.Id == fileTransfer.Id).Count();
                if (failedMatches > 0) return;

                InProgressFileTransfers.RemoveAll(f => f.Id == fileTransfer.Id);
                FailedFileTransfers.Add(fileTransfer);
            }
        }

        Result<FileTransfer> GetInboundFileTransfer(SendFileRequest sendFileRequest)
        {
            FileTransfer inboundFileTransfer;
            var fileTransferId = sendFileRequest.RemoteServerTransferId;

            if (fileTransferId > 0)
            {
                var getinboundFileTransfer = GetFileTransferById(fileTransferId);
                if (getinboundFileTransfer.Failure)
                {
                    return Result.Fail<FileTransfer>(getinboundFileTransfer.Error);
                }

                inboundFileTransfer = getinboundFileTransfer.Value;
                UpdateInboundFileTransferDetails(sendFileRequest, inboundFileTransfer);

                return Result.Ok(inboundFileTransfer);
            }

            inboundFileTransfer =
                InitializeFileTransfer(
                    TransferDirection.Inbound,
                    FileTransferInitiator.RemoteServer,
                    sendFileRequest.RemoteServerInfo,
                    sendFileRequest.FileName,
                    sendFileRequest.FileSizeInBytes,
                    sendFileRequest.LocalFolderPath,
                    sendFileRequest.RemoteFolderPath,
                    sendFileRequest.FileTransferResponseCode,
                    sendFileRequest.RetryLimit,
                    0);

            return Result.Ok(inboundFileTransfer);
        }

        Result<FileTransfer> GetFileTransferById(int id)
        {
            var matches = GetAllFileTransfers().Select(t => t).Where(t => t.Id == id).ToList();
            if (matches.Count == 0)
            {
                return Result.Fail<FileTransfer>(
                    $"No file transfer was found with an ID value of {id}");
            }

            return matches.Count == 1
                ? Result.Ok(matches[0])
                : Result.Fail<FileTransfer>(
                    $"Found {matches.Count} file transfers with the same ID value of {id}");
        }

        Result<FileTransfer> GetFileTransferByResponseCode(FileTransferResponse fileTransferResponse)
        {
            var responseCode = fileTransferResponse.TransferResponseCode;
            var remoteServerInfo = fileTransferResponse.RemoteServerInfo;

            var matches =
                GetAllFileTransfers().Select(t => t)
                    .Where(t => t.TransferResponseCode == responseCode)
                    .ToList();

            if (matches.Count == 0)
            {
                var error =
                    $"No file transfer was found with a response code value of {responseCode} " +
                    $"(Request received from {remoteServerInfo.SessionIpAddress}:{remoteServerInfo.PortNumber})";

                return Result.Fail<FileTransfer>(error);
            }

            return matches.Count == 1
                ? Result.Ok(matches[0])
                : Result.Fail<FileTransfer>(
                    $"Found {matches.Count} file transfers with the same response code value of {responseCode}");
        }

        void UpdateInboundFileTransferDetails(SendFileRequest sendFileRequest, FileTransfer fileTransfer)
        {
            fileTransfer.TransferResponseCode = sendFileRequest.FileTransferResponseCode;
            fileTransfer.FileSizeInBytes = sendFileRequest.FileSizeInBytes;
            fileTransfer.RemoteServerRetryLimit = sendFileRequest.RetryLimit;

            if (sendFileRequest.RetryCounter == 1) return;

            fileTransfer.RetryCounter = sendFileRequest.RetryCounter;
            fileTransfer.Status = FileTransferStatus.Pending;
            fileTransfer.ErrorMessage = string.Empty;

            fileTransfer.RequestInitiatedTime = DateTime.Now;
            fileTransfer.TransferStartTime = DateTime.MinValue;
            fileTransfer.TransferCompleteTime = DateTime.MinValue;

            fileTransfer.CurrentBytesReceived = 0;
            fileTransfer.TotalBytesReceived = 0;
            fileTransfer.BytesRemaining = 0;
            fileTransfer.PercentComplete = 0;
        }

        void HandleServerEventOccurred(object sender, ServerEvent serverEvent)
        {
            EventOccurred?.Invoke(sender, serverEvent);
        }

        protected virtual void OnEventOccurred(ServerEvent serverEvent)
        {
            var handler = EventOccurred;
            handler?.Invoke(this, serverEvent);
        }

        void HandleSocketEventOccurred(object sender, ServerEvent serverEvent)
        {
            SocketEventOccurred?.Invoke(sender, serverEvent);
        }

        protected virtual void OnSocketEventOccurred(ServerEvent serverEvent)
        {
            var handler = SocketEventOccurred;
            handler?.Invoke(this, serverEvent);
        }

        void HandleFileTransferProgress(object sender, ServerEvent serverEvent)
        {
            FileTransferProgress?.Invoke(sender, serverEvent);
        }

        protected virtual void OnFileTransferProgress(ServerEvent serverEvent)
        {
            var handler = FileTransferProgress;
            handler?.Invoke(this, serverEvent);
        }

        void ReportError(string error)
        {
            ErrorOccurred?.Invoke(this, error);
        }

        List<FileTransfer> GetAllFileTransfers()
        {
            var allFileTransfers = new List<FileTransfer>();
            allFileTransfers.AddRange(PendingFileTransfers);
            allFileTransfers.AddRange(InProgressFileTransfers);
            allFileTransfers.AddRange(ReceivedFileTransfers);
            allFileTransfers.AddRange(SentFileTransfers);
            allFileTransfers.AddRange(RejectedFileTransfers);
            allFileTransfers.AddRange(StalledFileTransfers);
            allFileTransfers.AddRange(FailedFileTransfers);

            return allFileTransfers;
        }

        List<FileTransfer> DeepCopyAllFileTransfers()
        {
            var allFileTransfers = new List<FileTransfer>();
            allFileTransfers.AddRange(PendingFileTransfers.Select(ft => ft.Duplicate()));
            allFileTransfers.AddRange(InProgressFileTransfers.Select(ft => ft.Duplicate()));
            allFileTransfers.AddRange(ReceivedFileTransfers.Select(ft => ft.Duplicate()));
            allFileTransfers.AddRange(SentFileTransfers.Select(ft => ft.Duplicate()));
            allFileTransfers.AddRange(RejectedFileTransfers.Select(ft => ft.Duplicate()));
            allFileTransfers.AddRange(StalledFileTransfers.Select(ft => ft.Duplicate()));
            allFileTransfers.AddRange(FailedFileTransfers.Select(ft => ft.Duplicate()));

            return allFileTransfers;
        }
    }
}
