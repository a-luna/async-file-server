namespace AaronLuna.AsyncFileServer.Controller
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Model;

    public class ServerState
    {
        readonly AsyncFileServer _localServer;

        public ServerState(AsyncFileServer localServer)
        {
            _localServer = localServer;
        }

        public bool NoFileTransfersPending => PendingTransferCount() == 0;
        public bool FileTransferPending => PendingTransferCount() > 0;
        public int PendingFileTransferCount => PendingTransferCount();
        public List<int> PendingFileTransferIds => GetPendingFileTransferIds();

        public bool NoRequests => RequestCount() > 0;
        public List<int> RequestIds => AllRequestIds();
        public DateTime MostRecentRequestTime => MostRecentRequestTimeStamp();

        public bool AllErrorsHaveBeenRead => UnreadErrorCount() == 0;
        public List<ServerError> UnreadErrors => GetUnreadErrors();

        public bool NoTextSessions => NoValidTextSessions();
        public List<int> TextSessionIds => AllTextSessionIds();
        public int UnreadTextMessageCount => GetNumberOfUnreadTextMessages();
        public List<int> TextSessionIdsWithUnreadMessages => GetTextSessionIdsWithUnreadMessages();

        public bool NoFileTransfers => FileTransferCount() > 0;
        public List<int> FileTransferIds => AllFileTransferIds();
        public int MostRecentFileTransferId => MostRecentTransferId();
        public List<int> StalledFileTransferIds => StalledTransferIds();

        int PendingTransferCount()
        {
            var pendingTransfers =
                _localServer.FileTransfers.Select(ft => ft)
                    .Where(ft => ft.TransferDirection == FileTransferDirection.Inbound
                                 && ft.Status == FileTransferStatus.Pending)
                    .ToList();

            return pendingTransfers.Count;
        }

        List<int> GetPendingFileTransferIds()
        {
            return
                _localServer.FileTransfers.Select(ft => ft)
                    .Where(ft => ft.TransferDirection == FileTransferDirection.Inbound
                                 && ft.Status == FileTransferStatus.Pending)
                    .Select(ft => ft.Id)
                    .ToList();
        }

        int RequestCount()
        {
            return _localServer.Requests.Count;
        }

        List<int> AllRequestIds()
        {
            return _localServer.Requests.Select(r => r.Id).ToList();
        }

        DateTime MostRecentRequestTimeStamp()
        {
            var requestsDesc =
                _localServer.Requests.Select(r => r)
                    .OrderByDescending(r => r.TimeStamp)
                    .ToList();

            return requestsDesc.Count == 0
                ? DateTime.MinValue
                : requestsDesc[0].TimeStamp;
        }

        int UnreadErrorCount()
        {
            var unreadErrors =
                _localServer.ErrorLog.Select(e => e)
                    .Where(e => e.Unread)
                    .ToList();

            return unreadErrors.Count;
        }

        List<ServerError> GetUnreadErrors()
        {
            return
                _localServer.ErrorLog.Select(e => e)
                    .Where(e => e.Unread)
                    .ToList();
        }

        bool NoValidTextSessions()
        {
            var validTextSessions =
                _localServer.TextSessions.Select(ts => ts)
                    .Where(ts => ts.MessageCount > 0)
                    .ToList();

            return validTextSessions.Count == 0;
        }

        List<int> AllTextSessionIds()
        {
            return _localServer.TextSessions.Select(t => t.Id).ToList();
        }

        int GetNumberOfUnreadTextMessages()
        {
            var unreadCount = 0;
            foreach (var textSession in _localServer.TextSessions)
            {
                foreach (var textMessage in textSession.Messages)
                {
                    if (textMessage.Unread)
                    {
                        unreadCount++;
                    }
                }
            }

            return unreadCount;
        }

        List<int> GetTextSessionIdsWithUnreadMessages()
        {
            var sessionIds = new List<int>();
            foreach (var textSession in _localServer.TextSessions)
            {
                foreach (var textMessage in textSession.Messages)
                {
                    if (textMessage.Unread)
                    {
                        sessionIds.Add(textSession.Id);
                    }
                }
            }

            return sessionIds.Distinct().ToList();
        }

        int FileTransferCount()
        {
            return _localServer.FileTransfers.Count;
        }

        List<int> AllFileTransferIds()
        {
            return _localServer.FileTransfers.Select(ft => ft.Id).ToList();
        }

        int MostRecentTransferId()
        {
            return FileTransferCount() == 0
                ? 0
                : AllFileTransferIds().Last();
        }

        List<int> StalledTransferIds()
        {
            return
                _localServer.FileTransfers.Select(t => t)
                    .Where(t => t.TransferStalled)
                    .Select(t => t.Id).ToList();
        }
    }
}
