namespace AaronLuna.AsyncFileServer.Model
{
    using System;
    using System.Collections.Generic;

    public class ServerRequest
    {
        public ServerRequest()
        {
            EventLog = new List<ServerEvent>();
            Type = ServerRequestType.None;
            Timestamp = DateTime.Now;
        }

        public int Id { get; set; }
        public DateTime Timestamp { get; }
        public ServerRequestType Type { get; set; }
        public List<ServerEvent> EventLog { get; set; }
        public ServerInfo RemoteServerInfo { get; set; }
        
        public override string ToString()
        {
            return $"{Type.Name()} from {RemoteServerInfo.SessionIpAddress}:{RemoteServerInfo.PortNumber} at {Timestamp:g}";
        }

        public bool ProcessRequestImmediately => Type.ProcessRequestImmediately();
        public bool IsFileTransferResponse => Type.IsFileTransferResponse();
        public bool IsFIleTransferError => Type.IsFileTransferError();
    }
}
