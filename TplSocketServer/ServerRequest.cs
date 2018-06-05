namespace TplSockets
{
    using System;
    using System.Collections.Generic;
    using System.Net;

    public class ServerRequest
    {
        public ServerRequest()
        {
            EventLog = new List<ServerEvent>();
            Type = RequestType.None;
        }

        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public byte[] Data { get; set; }
        public RequestType Type { get; set; }
        public IPAddress RemoteServerIp { get; set; }
        public List<ServerEvent> EventLog { get; set; }

        public override string ToString()
        {
            return $"{Type.Name()} from {RemoteServerIp} at {Timestamp:g}";
        }

        public bool ProcessRequestImmediately()
        {
            return Type.ProcessRequestImmediately();
        }
    }

    public static class ServerRequestExtensions
    {
        public static bool ProcessRequestImmediately(this ServerRequest request)
        {
            return request.Type.ProcessRequestImmediately();
        }
    }
}
