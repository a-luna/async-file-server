using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Xml.Serialization;
using AaronLuna.Common.Network;
using AaronLuna.Common.Result;

namespace TplSocketServer
{
    using System.Collections.Generic;

    public class AppSettings
    {
        float _transferFolderPath;

        public AppSettings()
        {
            TransferFolderPath = string.Empty;
            SocketSettings = new SocketSettings();
            RemoteServers = new List<RemoteServer>();
        }

        [XmlIgnore]
        public float TransferUpdateInterval
        {
            get => _transferFolderPath;
            set => _transferFolderPath = value;
        }

        [XmlElement("TransferUpdateInterval")]
        public string CustomTransferUpdateInterval
        {
            get => TransferUpdateInterval.ToString("#0.0000", CultureInfo.InvariantCulture);
            set => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _transferFolderPath);
        }

        public int MaxDownloadAttempts { get; set; }
        public string TransferFolderPath { get; set; }
        
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
                var thisLocalIp = server.ConnectionInfo.LocalIpString;

                if (string.IsNullOrEmpty(thisLocalIp))
                {
                    server.ConnectionInfo.LocalIpAddress = IPAddress.None;
                }
                else
                {
                    var parseLocalIpResult = Network.ParseSingleIPv4Address(thisLocalIp);
                    if (parseLocalIpResult.Success)
                    {
                        server.ConnectionInfo.LocalIpAddress = parseLocalIpResult.Value;
                    }
                }

                var thisPublicIp = server.ConnectionInfo.PublicIpString;

                if (string.IsNullOrEmpty(thisPublicIp))
                {
                    server.ConnectionInfo.PublicIpAddress = IPAddress.None;
                }
                else
                {
                    var parsePublicIpResult = Network.ParseSingleIPv4Address(thisPublicIp);
                    if (parsePublicIpResult.Success)
                    {
                        server.ConnectionInfo.PublicIpAddress = parsePublicIpResult.Value;
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
