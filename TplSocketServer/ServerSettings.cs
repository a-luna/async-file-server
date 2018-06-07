namespace TplSockets
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Xml;
    using System.Xml.Serialization;

    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    public class ServerSettings
    {
        float _transferUpdateInterval;

        public ServerSettings()
        {
            LocalServerFolderPath = string.Empty;
            LocalNetworkCidrIp = string.Empty;
            SocketSettings = new SocketSettings();
            RemoteServers = new List<ServerInfo>();
        }

        [XmlIgnore]
        public float TransferUpdateInterval
        {
            get => _transferUpdateInterval;
            set => _transferUpdateInterval = value;
        }

        [XmlIgnore]
        public TimeSpan RetryLimitLockout { get; set; }

        [XmlIgnore]
        public TimeSpan FileTransferStalledTimeout { get; set; }

        [XmlElement("TransferUpdateInterval")]
        public string CustomFileTransferUpdateInterval
        {
            get => TransferUpdateInterval.ToString("#0.0000", CultureInfo.InvariantCulture);
            set => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _transferUpdateInterval);
        }

        [XmlElement(DataType = "duration", ElementName = "RetryLimitLockoutInMinutes")]
        public string CustomRetryLimitLockout
        {
            get => XmlConvert.ToString(RetryLimitLockout);
            set => RetryLimitLockout = string.IsNullOrEmpty(value)
                ? TimeSpan.FromMinutes(10)
                : XmlConvert.ToTimeSpan(value);
        }

        [XmlElement(DataType = "duration", ElementName = "FileTransferStalledTimeoutInSeconds")]
        public string CustomFileTransferStalledTimeout
        {
            get => XmlConvert.ToString(FileTransferStalledTimeout);
            set => FileTransferStalledTimeout = string.IsNullOrEmpty(value)
                ? TimeSpan.FromSeconds(5)
                : XmlConvert.ToTimeSpan(value);
        }

        public int TransferRetryLimit { get; set; }
        public string LocalServerFolderPath { get; set; }
        public int LocalServerPortNumber { get; set; }
        public string LocalNetworkCidrIp { get; set; }

        public SocketSettings SocketSettings { get; set; }
        public List<ServerInfo> RemoteServers { get; set; }

        public static Result<ServerSettings> ReadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                var defaultSettings = GetDefaultSettings();
                SaveToFile(defaultSettings, filePath);

                return Result.Ok(defaultSettings);
            }

            var deserializeResult = Deserialize(filePath);
            if (deserializeResult.Failure)
            {
                return deserializeResult;
            }

            var settings = deserializeResult.Value;
            settings.InitializeIpAddresses();

            return Result.Ok(settings);
        }

        public static Result<ServerSettings> Deserialize(string filePath)
        {
            ServerSettings settings;
            try
            {
                var deserializer = new XmlSerializer(typeof(ServerSettings));
                using (var reader = new StreamReader(filePath))
                {
                    settings = (ServerSettings)deserializer.Deserialize(reader);
                }
            }
            catch (Exception ex)
            {
                return Result.Fail<ServerSettings>($"{ex.Message} ({ex.GetType()})");
            }

            return Result.Ok(settings);
        }

        public static void SaveToFile(ServerSettings settings, string filePath)
        {
            var serializer = new XmlSerializer(typeof(ServerSettings));
            using (var writer = new StreamWriter(filePath))
            {
                serializer.Serialize(writer, settings);
            }
        }

        public void InitializeIpAddresses()
        {
            foreach (var server in RemoteServers)
            {
                var localIp = server.LocalIpString;

                if (string.IsNullOrEmpty(localIp))
                {
                    server.LocalIpAddress = IPAddress.None;
                }
                else
                {
                    var parseLocalIpResult = NetworkUtilities.ParseSingleIPv4Address(localIp);
                    if (parseLocalIpResult.Success)
                    {
                        server.LocalIpAddress = parseLocalIpResult.Value;
                    }
                }

                var pubicIp = server.PublicIpString;

                if (string.IsNullOrEmpty(pubicIp))
                {
                    server.PublicIpAddress = IPAddress.None;
                }
                else
                {
                    var parsePublicIpResult = NetworkUtilities.ParseSingleIPv4Address(pubicIp);
                    if (parsePublicIpResult.Success)
                    {
                        server.PublicIpAddress = parsePublicIpResult.Value;
                    }
                }

                var sessionIp = server.SessionIpString;

                if (string.IsNullOrEmpty(sessionIp))
                {
                    server.SessionIpAddress = IPAddress.None;
                }
                else
                {
                    var parseSessionIpResult = NetworkUtilities.ParseSingleIPv4Address(sessionIp);
                    if (parseSessionIpResult.Success)
                    {
                        server.SessionIpAddress = parseSessionIpResult.Value;
                    }
                }
            }
        }

        static ServerSettings GetDefaultSettings()
        {
            var defaultTransferFolderPath
                = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}transfer";

            return new ServerSettings
            {
                TransferRetryLimit = 3,
                LocalServerFolderPath = defaultTransferFolderPath,
                TransferUpdateInterval = 0.0025f,
                LocalNetworkCidrIp = string.Empty,
                LocalServerPortNumber = 0
            };
        }
    }
}
