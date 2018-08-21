using System;
using AaronLuna.AsyncSocketServer;
using AaronLuna.AsyncSocketServer.FileTransfers;
using AaronLuna.AsyncSocketServer.Requests.RequestTypes;

namespace AaronLuna.AsyncSocketServerTest.TestClasses
{
    public class SpecialAsyncServer : AsyncServer
    {
        SpecialFileTransferHandler _specialFileTransferHandler;

        public SpecialAsyncServer(ServerSettings settings) : base(settings)
        {
            FileTransferHandler = new SpecialFileTransferHandler(Settings);
            FileTransferHandler.EventOccurred += HandleServerEvent;
            FileTransferHandler.SocketEventOccurred += HandleSocketEvent;
            FileTransferHandler.FileTransferProgress += HandleFileTransferProgress;
            FileTransferHandler.ErrorOccurred += HandleErrorOccurred;
            FileTransferHandler.PendingFileTransfer += HandlePendingFileTransfer;
            FileTransferHandler.InboundFileAlreadyExists += SendNotificationFileAlreadyExists;
            FileTransferHandler.InboundFileTransferComplete += HandleInboundFileTransferComplete;
            FileTransferHandler.RequestedFileDoesNotExist += SendNotificationRequestedFileDoesNotExist;
            FileTransferHandler.ReceivedRetryOutboundFileTransferRequest += ReceivedRetryOutboundFileTransferRequest;
            FileTransferHandler.RetryLimitLockoutExpired += HandleRetryLimitLockoutExpired;

            _specialFileTransferHandler = (SpecialFileTransferHandler) FileTransferHandler;
        }

        public void UseMockTimeFileSender(TimeSpan duration)
        {
            _specialFileTransferHandler.UseMockTimeFileSender(duration);
        }

        public void UseMockStalledFileSender()
        {
            _specialFileTransferHandler.UseMockStalledFileSender();
        }

        public void UseOriginalFileSender()
        {
            _specialFileTransferHandler.UseDefaultFileSender();
        }

        public void UseMockTimeFileReceiver(TimeSpan duration)
        {
            _specialFileTransferHandler.UseMockTimeFileReceiver(duration);
        }

        public void UseOriginalFileReceiver()
        {
            _specialFileTransferHandler.UseDefaultFileReceiver();
        }

        void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            OnEventOccurred(serverEvent);
        }

        void HandleSocketEvent(object sender, ServerEvent serverEvent)
        {
            OnSocketEventOccurred(serverEvent);
        }

        void HandleFileTransferProgress(object sender, ServerEvent serverEvent)
        {
            OnFileTransferProgress(serverEvent);
        }
    }
}
