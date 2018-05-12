namespace TplSockets
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Xml.Serialization;

    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    public class AppSettings
    {
        float _fileTransferFolderPath;

        public AppSettings()
        {
            LocalServerFolderPath = string.Empty;
            LocalNetworkCidrIp = string.Empty;
            SocketSettings = new SocketSettings();
            RemoteServers = new List<RemoteServer>();
        }

        [XmlIgnore]
        public float FileTransferUpdateInterval
        {
            get => _fileTransferFolderPath;
            set => _fileTransferFolderPath = value;
        }

        [XmlElement("FileTransferUpdateInterval")]
        public string CustomFileTransferUpdateInterval
        {
            get => FileTransferUpdateInterval.ToString("#0.0000", CultureInfo.InvariantCulture);
            set => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _fileTransferFolderPath);
        }

        public int MaxDownloadAttempts { get; set; }
        public string LocalServerFolderPath { get; set; }
        public int LocalPort { get; set; }
        public string LocalNetworkCidrIp { get; set; }

        public SocketSettings SocketSettings { get; set; }
        public List<RemoteServer> RemoteServers { get; set; }

        public static void SaveToFile(AppSettings settings, string filePath)
        {
            AppSettings.Serialize(settings, filePath);
        }

        static void Serialize(AppSettings settings, string filePath)
        {
            var serializer = new XmlSerializer(typeof(AppSettings));
            using (var writer = new StreamWriter(filePath))
            {
                serializer.Serialize(writer, settings);
            }
        }

        public void InitializeIpAddresses()
        {
            foreach (var server in RemoteServers)
            {
                var localIp = server.ConnectionInfo.LocalIpString;

                if (string.IsNullOrEmpty(localIp))
                {
                    server.ConnectionInfo.LocalIpAddress = IPAddress.None;
                }
                else
                {
                    var parseLocalIpResult = NetworkUtilities.ParseSingleIPv4Address(localIp);
                    if (parseLocalIpResult.Success)
                    {
                        server.ConnectionInfo.LocalIpAddress = parseLocalIpResult.Value;
                    }
                }

                var pubicIp = server.ConnectionInfo.PublicIpString;

                if (string.IsNullOrEmpty(pubicIp))
                {
                    server.ConnectionInfo.PublicIpAddress = IPAddress.None;
                }
                else
                {
                    var parsePublicIpResult = NetworkUtilities.ParseSingleIPv4Address(pubicIp);
                    if (parsePublicIpResult.Success)
                    {
                        server.ConnectionInfo.PublicIpAddress = parsePublicIpResult.Value;
                    }
                }

                var sessionIp = server.ConnectionInfo.SessionIpString;

                if (string.IsNullOrEmpty(sessionIp))
                {
                    server.ConnectionInfo.SessionIpAddress = IPAddress.None;
                }
                else
                {
                    var parseSessionIpResult = NetworkUtilities.ParseSingleIPv4Address(sessionIp);
                    if (parseSessionIpResult.Success)
                    {
                        server.ConnectionInfo.SessionIpAddress = parseSessionIpResult.Value;
                    }
                }
            }
        }

        public static Result<AppSettings> Deserialize(string filePath)
        {
            AppSettings settings;
            try
            {
                var deserializer = new XmlSerializer(typeof(AppSettings));
                using (var reader = new StreamReader(filePath))
                {
                    settings = (AppSettings)deserializer.Deserialize(reader);
                }
            }
            catch (Exception ex)
            {
                return Result.Fail<AppSettings>($"{ex.Message} ({ex.GetType()})");
            }

            settings.InitializeIpAddresses();

            return Result.Ok(settings);
        }
    }
}
