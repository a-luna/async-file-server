using System.IO;
using System.Xml.Serialization;

namespace TplSocketServer
{
    using System.Collections.Generic;

    public class AppSettings
    {
        public AppSettings()
        {
            TransferFolderPath = string.Empty;
            SocketSettings = new SocketSettings();
            RemoteServers = new List<RemoteServer>();
        }

        public string TransferFolderPath { get; set; }
        public SocketSettings SocketSettings { get; set; }
        public List<RemoteServer> RemoteServers { get; set; }

        public static void Serialize(AppSettings settings, string filePath)
        {
            var serializer = new XmlSerializer(typeof(AppSettings));
            using (var writer = new StreamWriter(filePath))
            {
                serializer.Serialize(writer, settings);
            }
        }

        public static AppSettings Deserialize(string filePath)
        {
            AppSettings settings;
            var deserializer = new XmlSerializer(typeof(AppSettings));
            using (var reader = new StreamReader(filePath))
            {
                settings = (AppSettings) deserializer.Deserialize(reader);
            }

            return settings;
        }
    }
}
