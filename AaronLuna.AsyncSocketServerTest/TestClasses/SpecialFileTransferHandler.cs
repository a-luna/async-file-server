using System;
using AaronLuna.AsyncSocketServer;
using AaronLuna.AsyncSocketServer.FileTransfers;

namespace AaronLuna.AsyncSocketServerTest.TestClasses
{
    class SpecialFileTransferHandler : FileTransferHandler
    {
        public SpecialFileTransferHandler(ServerSettings settings) : base(settings) { }

        public void UseMockTimeFileSender(TimeSpan duration)
        {
            FileSender = new MockTimeFileSender(Settings, duration);
            FileSender.EventOccurred += HandleServerEventOccurred;
            FileSender.SocketEventOccurred += HandleSocketEventOccurred;
            FileSender.FileTransferProgress += HandleFileTransferProgress;
        }

        public void UseMockStalledFileSender()
        {
            FileSender = new MockStalledFileSender(Settings);
            FileSender.EventOccurred += HandleServerEventOccurred;
            FileSender.SocketEventOccurred += HandleSocketEventOccurred;
            FileSender.FileTransferProgress += HandleFileTransferProgress;
        }

        public void UseDefaultFileSender()
        {
            FileSender = new FileSender(Settings);
            FileSender.EventOccurred += HandleServerEventOccurred;
            FileSender.SocketEventOccurred += HandleSocketEventOccurred;
            FileSender.FileTransferProgress += HandleFileTransferProgress;
        }

        public void UseMockTimeFileReceiver(TimeSpan duration)
        {
            FileReceiver = new MockTimeFileReceiver(Settings, duration);
            FileReceiver.EventOccurred += HandleServerEventOccurred;
            FileReceiver.SocketEventOccurred += HandleSocketEventOccurred;
            FileReceiver.FileTransferProgress += HandleFileTransferProgress;
        }

        public void UseDefaultFileReceiver()
        {
            FileReceiver = new FileReceiver(Settings);
            FileReceiver.EventOccurred += HandleServerEventOccurred;
            FileReceiver.SocketEventOccurred += HandleSocketEventOccurred;
            FileReceiver.FileTransferProgress += HandleFileTransferProgress;
        }

        void HandleServerEventOccurred(object sender, ServerEvent serverEvent)
        {
            OnEventOccurred(serverEvent);
        }

        void HandleSocketEventOccurred(object sender, ServerEvent serverEvent)
        {
            OnSocketEventOccurred(serverEvent);
        }

        void HandleFileTransferProgress(object sender, ServerEvent serverEvent)
        {
            OnFileTransferProgress(serverEvent);
        }
    }
}
