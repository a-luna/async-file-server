using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.SocketExtensions;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.Requests
{
    public class RequestSender
    {
        readonly ServerInfo _localServerInfo;
        readonly int _timeoutMs;
        Socket _socket;

        public RequestSender(ServerInfo localServerInfo, SocketSettings settings)
        {
            _localServerInfo = localServerInfo;
            _timeoutMs = settings.SocketTimeoutInMilliseconds;
        }

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<Request> SuccessfullySentRequest;

        public async Task<Result> SendAsync(Request outboundRequest)
        {
            var encodeRequest = outboundRequest.EncodeRequest(_localServerInfo);
            if (encodeRequest.Failure)
            {
                return Result.Fail(encodeRequest.Error);
            }

            var requestBytes = encodeRequest.Value;
            var requestType = outboundRequest.Type;
            var remoteServerInfo = outboundRequest.RemoteServerInfo;

            EventOccurred?.Invoke(this, GetSendRequestStartedEvent(requestType, remoteServerInfo));

            var connectToServer = await ConnectToServerAsync(remoteServerInfo).ConfigureAwait(false);
            if (connectToServer.Failure)
            {
                return connectToServer;
            }

            var sendRequestLength = await SendRequestLength(requestBytes).ConfigureAwait(false);
            if (sendRequestLength.Failure)
            {
                return sendRequestLength;
            }

            var sendRequest = await SendRequestBytes(requestBytes).ConfigureAwait(false);
            if (sendRequest.Failure)
            {
                return sendRequest;
            }

            if (requestType != RequestType.FileTransferAccepted)
            {
                ShutdownSocket();
            }

            EventOccurred?.Invoke(this, GetSendRequestCompleteEvent(requestType, remoteServerInfo));
            SuccessfullySentRequest?.Invoke(this, outboundRequest);

            return Result.Ok();
        }

        public Socket GetTransferSocket()
        {
            return _socket ?? new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public Result ShutdownSocket()
        {
            try
            {
                _socket.Shutdown(SocketShutdown.Send);
                _socket.Close();
            }
            catch (ObjectDisposedException ex)
            {
                return Result.Fail(ex.GetReport());
            }
            catch (SocketException ex)
            {
                return Result.Fail(ex.GetReport());
            }

            return Result.Ok();
        }

        async Task<Result> ConnectToServerAsync(ServerInfo remoteServerInfo)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.ConnectToRemoteServerStarted,
                RemoteServerIpAddress = remoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = remoteServerInfo.PortNumber
            });

            var connectToRemoteServer =
                await _socket.ConnectWithTimeoutAsync(
                        remoteServerInfo.SessionIpAddress,
                        remoteServerInfo.PortNumber,
                        _timeoutMs)
                    .ConfigureAwait(false);

            if (connectToRemoteServer.Failure)
            {
                return Result.Fail<Socket>(connectToRemoteServer.Error);
            }

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.ConnectToRemoteServerComplete,
                RemoteServerIpAddress = remoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = remoteServerInfo.PortNumber
            });

            return Result.Ok();
        }

        async Task<Result> SendRequestLength(byte[] encodedRequest)
        {
            var totalBytesInRequest = BitConverter.GetBytes(encodedRequest.Length);

            return await
                _socket.SendWithTimeoutAsync(
                        totalBytesInRequest,
                        0,
                        totalBytesInRequest.Length,
                        SocketFlags.None,
                        _timeoutMs)
                    .ConfigureAwait(false);
        }

        async Task<Result> SendRequestBytes(byte[] encodedRequest)
        {
            return await
                _socket.SendWithTimeoutAsync(
                        encodedRequest,
                        0,
                        encodedRequest.Length,
                        SocketFlags.None,
                        _timeoutMs)
                    .ConfigureAwait(false);
        }

        ServerEvent GetSendRequestStartedEvent(RequestType requestType, ServerInfo remoteServerInfo)
        {
            var eventDictionary = new Dictionary<RequestType, EventType>
            {
                {RequestType.ServerInfoRequest, EventType.RequestServerInfoStarted},
                {RequestType.ServerInfoResponse, EventType.SendServerInfoStarted},
                {RequestType.MessageRequest, EventType.SendTextMessageStarted},
                {RequestType.FileListRequest, EventType.RequestFileListStarted},
                {RequestType.FileListResponse, EventType.SendFileListStarted},
                {RequestType.RequestedFolderIsEmpty, EventType.SendNotificationFolderIsEmptyStarted},
                {RequestType.RequestedFolderDoesNotExist, EventType.SendNotificationFolderDoesNotExistStarted},
                {RequestType.RequestedFileDoesNotExist, EventType.SendNotificationFileDoesNotExistStarted},
                {RequestType.OutboundFileTransferRequest, EventType.RequestOutboundFileTransferStarted},
                {RequestType.InboundFileTransferRequest, EventType.RequestInboundFileTransferStarted},
                {RequestType.FileTransferRejected, EventType.SendFileTransferRejectedStarted},
                {RequestType.FileTransferAccepted, EventType.SendFileTransferAcceptedStarted},
                {RequestType.FileTransferComplete, EventType.SendFileTransferCompletedStarted},
                {RequestType.FileTransferStalled, EventType.SendFileTransferStalledStarted},
                {RequestType.RetryOutboundFileTransfer, EventType.RetryOutboundFileTransferStarted},
                {RequestType.RetryLimitExceeded, EventType.SendRetryLimitExceededStarted},
                {RequestType.ShutdownServerCommand, EventType.SendShutdownServerCommandStarted}
            };

            return new ServerEvent
            {
                EventType = eventDictionary[requestType],
                RemoteServerIpAddress = remoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = remoteServerInfo.PortNumber,
                RemoteFolder = remoteServerInfo.TransferFolder,
                LocalIpAddress = _localServerInfo.LocalIpAddress,
                LocalPortNumber = _localServerInfo.PortNumber,
                LocalFolder = _localServerInfo.TransferFolder
            };
        }

        ServerEvent GetSendRequestCompleteEvent(RequestType requestType, ServerInfo remoteServerInfo)
        {
            var eventDictionary = new Dictionary<RequestType, EventType>
            {
                {RequestType.ServerInfoRequest, EventType.RequestServerInfoComplete},
                {RequestType.ServerInfoResponse, EventType.SendServerInfoComplete},
                {RequestType.MessageRequest, EventType.SendTextMessageComplete},
                {RequestType.FileListRequest, EventType.RequestFileListComplete},
                {RequestType.FileListResponse, EventType.SendFileListComplete},
                {RequestType.RequestedFolderIsEmpty, EventType.SendNotificationFolderIsEmptyComplete},
                {RequestType.RequestedFolderDoesNotExist, EventType.SendNotificationFolderDoesNotExistComplete},
                {RequestType.RequestedFileDoesNotExist, EventType.SendNotificationFileDoesNotExistComplete},
                {RequestType.OutboundFileTransferRequest, EventType.RequestOutboundFileTransferComplete},
                {RequestType.InboundFileTransferRequest, EventType.RequestInboundFileTransferComplete},
                {RequestType.FileTransferRejected, EventType.SendFileTransferRejectedComplete},
                {RequestType.FileTransferAccepted, EventType.SendFileTransferAcceptedComplete},
                {RequestType.FileTransferComplete, EventType.SendFileTransferCompletedComplete},
                {RequestType.FileTransferStalled, EventType.SendFileTransferStalledComplete},
                {RequestType.RetryOutboundFileTransfer, EventType.RetryOutboundFileTransferComplete},
                {RequestType.RetryLimitExceeded, EventType.SendRetryLimitExceededCompleted},
                {RequestType.ShutdownServerCommand, EventType.SendShutdownServerCommandComplete}
            };

            return new ServerEvent
            {
                EventType = eventDictionary[requestType],
                RemoteServerIpAddress = remoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = remoteServerInfo.PortNumber,
                RemoteFolder = remoteServerInfo.TransferFolder,
                LocalIpAddress = _localServerInfo.LocalIpAddress,
                LocalPortNumber = _localServerInfo.PortNumber,
                LocalFolder = _localServerInfo.TransferFolder
            };
        }
    }
}
