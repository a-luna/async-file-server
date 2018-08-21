using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.SocketExtensions;
using AaronLuna.Common;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.Requests
{
    internal class RequestReceiver
    {
        Socket _socket;
        byte[] _buffer;
        int _bufferSize;
        int _timeoutMs;
        List<byte> _unreadBytes;
        readonly SocketSettings _settings;

        public RequestReceiver(SocketSettings settings)
        {
            _settings = settings;
        }

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;
        public event EventHandler<List<byte>> ReceivedFileBytes;

        public async Task<Result<byte[]>> ReceiveRequestAsync(Socket socket)
        {
            _socket = socket;
            _bufferSize = _settings.BufferSize;
            _timeoutMs = _settings.SocketTimeoutInMilliseconds;
            _buffer = new byte[_bufferSize];
            _unreadBytes = new List<byte>();

            EventOccurred?.Invoke(this,
                new ServerEvent { EventType = EventType.ReceiveRequestFromRemoteServerStarted });

            var getRequestLength = await ReceiveLengthOfIncomingRequest().ConfigureAwait(false);
            if (getRequestLength.Failure)
            {
                return Result.Fail<byte[]>(getRequestLength.Error);
            }

            var totalBytes = getRequestLength.Value;

            var getRequest = await ReceiveRequestBytesAsync(totalBytes).ConfigureAwait(false);
            if (getRequest.Failure)
            {
                return Result.Fail<byte[]>(getRequest.Error);
            }

            var encodedRequest = getRequest.Value;

            EventOccurred?.Invoke(this,
                new ServerEvent { EventType = EventType.ReceiveRequestFromRemoteServerComplete });

            return Result.Ok(encodedRequest);
        }

        public Socket GetTransferSocket()
        {
            return _socket ?? new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        async Task<Result<int>> ReceiveLengthOfIncomingRequest()
        {
            EventOccurred?.Invoke(this,
                new ServerEvent {EventType = EventType.ReceiveRequestLengthStarted});

            var readFromSocket = await
                _socket.ReceiveWithTimeoutAsync(
                        _buffer,
                        0,
                        _bufferSize,
                        SocketFlags.None,
                        _timeoutMs)
                    .ConfigureAwait(false);

            if (readFromSocket.Failure)
            {
                return readFromSocket;
            }

            var lastBytesReceivedCount = readFromSocket.Value;
            var unreadBytesCount = lastBytesReceivedCount - Constants.SizeOfInt32InBytes;
            var requestLength = BitConverter.ToInt32(_buffer, 0);

            var requestLengthBytes = new byte[Constants.SizeOfInt32InBytes];
            _buffer.ToList().CopyTo(0, requestLengthBytes, 0, Constants.SizeOfInt32InBytes);

            SocketEventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.ReceivedRequestLengthBytesFromSocket,
                BytesReceivedCount = lastBytesReceivedCount,
                RequestLengthInBytes = Constants.SizeOfInt32InBytes,
                UnreadBytesCount = unreadBytesCount
            });

            if (lastBytesReceivedCount > Constants.SizeOfInt32InBytes)
            {
                var unreadBytes = new byte[unreadBytesCount];
                _buffer.ToList().CopyTo(Constants.SizeOfInt32InBytes, unreadBytes, 0, unreadBytesCount);
                _unreadBytes = unreadBytes.ToList();

                EventOccurred?.Invoke(this, new ServerEvent
                {
                    EventType = EventType.SaveUnreadBytesAfterRequestLengthReceived,
                    UnreadBytesCount = unreadBytesCount
                });
            }

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.ReceiveRequestLengthComplete,
                RequestLengthInBytes = requestLength,
                RequestLengthBytes = requestLengthBytes
            });

            return Result.Ok(requestLength);
        }

        async Task<Result<byte[]>> ReceiveRequestBytesAsync(int totalBytes)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent {EventType = EventType.ReceiveRequestBytesStarted});

            int currentRequestBytesReceived;
            var socketReadCount = 0;
            var bytesReceived = 0;
            var bytesRemaining = totalBytes;
            var requestBytes = new List<byte>();
            var unreadByteCount = 0;

            if (_unreadBytes.Count > 0)
            {
                currentRequestBytesReceived = Math.Min(totalBytes, _unreadBytes.Count);
                var unreadRequestBytes = new byte[currentRequestBytesReceived];

                _unreadBytes.CopyTo(0, unreadRequestBytes, 0, currentRequestBytesReceived);
                requestBytes.AddRange(unreadRequestBytes.ToList());

                bytesReceived += currentRequestBytesReceived;
                bytesRemaining -= currentRequestBytesReceived;

                EventOccurred?.Invoke(this, new ServerEvent
                {
                    EventType = EventType.CopySavedBytesToRequestData,
                    UnreadBytesCount = _unreadBytes.Count,
                    RequestLengthInBytes = totalBytes,
                    RequestBytesRemaining = bytesRemaining
                });

                if (_unreadBytes.Count > totalBytes)
                {
                    var fileByteCount = _unreadBytes.Count - totalBytes;
                    var fileBytes = new byte[fileByteCount];
                    _unreadBytes.CopyTo(totalBytes, fileBytes, 0, fileByteCount);
                    _unreadBytes = fileBytes.ToList();
                }
                else
                {
                    _unreadBytes = new List<byte>();
                }
            }

            currentRequestBytesReceived = 0;
            while (bytesRemaining > 0)
            {
                var readFromSocket =
                    await _socket.ReceiveWithTimeoutAsync(
                            _buffer,
                            0,
                            _bufferSize,
                            SocketFlags.None,
                            _timeoutMs)
                        .ConfigureAwait(false);

                if (readFromSocket.Failure)
                {
                    return Result.Fail<byte[]>(readFromSocket.Error);
                }

                var lastBytesReceivedCount = readFromSocket.Value;
                currentRequestBytesReceived = Math.Min(bytesRemaining, lastBytesReceivedCount);

                var requestBytesFromSocket = new byte[currentRequestBytesReceived];
                _buffer.ToList().CopyTo(0, requestBytesFromSocket, 0, currentRequestBytesReceived);
                requestBytes.AddRange(requestBytesFromSocket.ToList());

                socketReadCount++;
                unreadByteCount = lastBytesReceivedCount - currentRequestBytesReceived;
                bytesReceived += currentRequestBytesReceived;
                bytesRemaining -= currentRequestBytesReceived;

                SocketEventOccurred?.Invoke(this, new ServerEvent
                {
                    EventType = EventType.ReceivedRequestBytesFromSocket,
                    SocketReadCount = socketReadCount,
                    BytesReceivedCount = lastBytesReceivedCount,
                    CurrentRequestBytesReceived = currentRequestBytesReceived,
                    TotalRequestBytesReceived = bytesReceived,
                    RequestLengthInBytes = totalBytes,
                    RequestBytesRemaining = bytesRemaining,
                    UnreadBytesCount = unreadByteCount
                });
            }

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.ReceiveRequestBytesComplete,
                RequestBytes = requestBytes.ToArray()
            });

            if (unreadByteCount == 0) return Result.Ok(requestBytes.ToArray());

            var unreadBytes = new byte[unreadByteCount];
            _buffer.ToList().CopyTo(currentRequestBytesReceived, unreadBytes, 0, unreadByteCount);
            _unreadBytes = unreadBytes.ToList();

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.SaveUnreadBytesAfterAllRequestBytesReceived,
                ExpectedByteCount = currentRequestBytesReceived,
                UnreadBytesCount = unreadByteCount
            });

            ReceivedFileBytes?.Invoke(this, _unreadBytes);
            return Result.Ok(requestBytes.ToArray());
        }
    }
}
