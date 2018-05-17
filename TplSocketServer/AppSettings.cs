﻿namespace TplSockets
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
            RemoteServers = new List<ServerInfo>();
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
        public List<ServerInfo> RemoteServers { get; set; }

        public static Result<AppSettings> ReadFromFile(string filePath)
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
        
        public static void SaveToFile(AppSettings settings, string filePath)
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
    }
}
