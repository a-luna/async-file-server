using System.IO;
using System.Xml.Serialization;

namespace TplSocketServer
{
    using System.Collections.Generic;

    public class ServerSettings
    {
        public ServerSettings()
        {
            TransferFolderPath = string.Empty;
            SocketSettings = new SocketSettings();
            RemoteServers = new List<ServerInfo>();
        }

        public int PortNumber { get; set; }
        public string TransferFolderPath { get; set; }
        public SocketSettings SocketSettings { get; set; }
        public List<ServerInfo> RemoteServers { get; set; }

        public static void Serialize(ServerSettings settings, string filePath)
        {
            var serializer = new XmlSerializer(typeof(ServerSettings));
            using (var writer = new StreamWriter(filePath))
            {
                serializer.Serialize(writer, settings);
            }
        }

        public static ServerSettings Deserialize(string filePath)
        {
            ServerSettings settings = new ServerSettings();
            var deserializer = new XmlSerializer(typeof(ServerSettings));
            using (var reader = new StreamReader(filePath))
            {
                settings = (ServerSettings) deserializer.Deserialize(reader);
            }

            return settings;
        }
    }
}
