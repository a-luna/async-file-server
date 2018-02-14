using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace TplSocketsTest
{
    using System.Text;
    using System.Threading.Tasks;

    using TplSocketServer;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class TplSocketTestFixture
    {
        const int BufferSize = 8 * 1024;
        const int ConnectTimeoutMs = 3000;
        const int ReceiveTimeoutMs = 3000;
        const int SendTimeoutMs = 3000;

        Socket _listenSocket;
        Socket _serverSocket;
        Socket _clientSocket;

        string _serverIpAddress;
        string _messageReceived;

        [TestInitialize]
        public void TestSetup()
        {
            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            var myIp = IpAddressHelper.GetLocalIpV4Address();
            _serverIpAddress = myIp.ToString();

            _messageReceived = string.Empty;
        }

        [TestMethod]
        public async Task TestAcceptConnectionAsync()
        {
            var serverPort = 7002;

            Assert.IsFalse(_serverSocket.Connected);
            Assert.IsFalse(_serverSocket.IsBound);
            Assert.IsNull(_serverSocket.LocalEndPoint);
            Assert.IsNull(_serverSocket.RemoteEndPoint);

            Assert.IsFalse(_clientSocket.Connected);
            Assert.IsFalse(_clientSocket.IsBound);
            Assert.IsNull(_clientSocket.LocalEndPoint);
            Assert.IsNull(_clientSocket.RemoteEndPoint);

            var ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            var ipAddress = ipHostInfo.AddressList.Select(ip => ip)
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);

            var ipEndPoint = new IPEndPoint(ipAddress, serverPort);

            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listenSocket.Bind(ipEndPoint);
            _listenSocket.Listen(10);

            var acceptTask = Task.Run(AcceptConnectionTask);

            var connectResult = await _clientSocket.ConnectWithTimeoutAsync(_serverIpAddress, serverPort, ConnectTimeoutMs).ConfigureAwait(false);
            if (connectResult.Failure)
            {
                Assert.Fail("There was an error connecting to the server." + connectResult.Error);
            }

            var acceptResult = await acceptTask.ConfigureAwait(false);
            if (acceptResult.Failure)
            {
                Assert.Fail("There was an error accepting the incoming connection: " + acceptResult.Error);
            }

            _serverSocket = acceptResult.Value;

            Assert.IsTrue(_serverSocket.Connected);
            Assert.IsTrue(_serverSocket.IsBound);
            Assert.IsNotNull(_serverSocket.LocalEndPoint);
            Assert.IsNotNull(_serverSocket.RemoteEndPoint);

            Assert.IsTrue(_clientSocket.Connected);
            Assert.IsTrue(_clientSocket.IsBound);
            Assert.IsNotNull(_clientSocket.LocalEndPoint);
            Assert.IsNotNull(_clientSocket.RemoteEndPoint);

            Assert.AreEqual(_serverSocket.LocalEndPoint, _clientSocket.RemoteEndPoint);
            Assert.AreEqual(_serverSocket.RemoteEndPoint, _clientSocket.LocalEndPoint);
        }

        [TestMethod]
        public async Task TestSendAndReceiveTextMesageAsync()
        {
            var serverPort = 7003;

            var ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            var ipAddress = ipHostInfo.AddressList.Select(ip => ip)
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);

            var ipEndPoint = new IPEndPoint(ipAddress, serverPort);

            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listenSocket.Bind(ipEndPoint);
            _listenSocket.Listen(10);

            var acceptTask = Task.Run(AcceptConnectionTask);

            var connectResult = await _clientSocket.ConnectWithTimeoutAsync(_serverIpAddress, serverPort, ConnectTimeoutMs).ConfigureAwait(false);
            if (connectResult.Failure)
            {
                Assert.Fail("There was an error connecting to the server." + connectResult.Error);
            }

            var acceptResult = await acceptTask.ConfigureAwait(false);
            if (acceptResult.Failure)
            {
                Assert.Fail("There was an error accepting the incoming connection: " + acceptResult.Error);
            }

            _serverSocket = acceptResult.Value;

            Assert.AreEqual(string.Empty, _messageReceived);

            var receiveMessageTask = Task.Run(ReceiveMessageAsync);

            var messageSent = "this is a text message from a socket";
            var messageData = Encoding.ASCII.GetBytes(messageSent);
            var sendMessageResult = await _clientSocket
                                        .SendWithTimeoutAsync(messageData, 0, messageData.Length, 0, SendTimeoutMs)
                                        .ConfigureAwait(false);

            var receiveMessageResult = await receiveMessageTask.ConfigureAwait(false);

            if (Result.Combine(sendMessageResult, receiveMessageResult).Failure)
            {
                Assert.Fail("There was an error sending/receiving the text message");
            }

            Assert.AreEqual(messageSent, _messageReceived);
        }

        async Task<Result<Socket>> AcceptConnectionTask()
        {
            return await _listenSocket.AcceptTaskAsync().ConfigureAwait(false);
        }

        async Task<Result<int>> ReceiveMessageAsync()
        {
            var buffer = new byte[BufferSize];
            var receiveResult = await _serverSocket
                                    .ReceiveWithTimeoutAsync(buffer, 0, BufferSize, 0, ReceiveTimeoutMs)
                                    .ConfigureAwait(false);

            var bytesReceived = receiveResult.Value;
            if (bytesReceived > 0)
            {
                _messageReceived = Encoding.ASCII.GetString(buffer, 0, bytesReceived);
            }

            return receiveResult;
        }
    }
}
