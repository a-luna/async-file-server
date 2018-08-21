using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer
{
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

        public LogLevel LogLevel { get; set; }
        public int TransferRetryLimit { get; set; }
        public string LocalServerFolderPath { get; set; }
        public int LocalServerPortNumber { get; set; }
        public string LocalNetworkCidrIp { get; set; }

        public SocketSettings SocketSettings { get; set; }
        public List<ServerInfo> RemoteServers { get; set; }

        public static ServerSettings GetDefaultSettings()
        {
            var defaultTransferFolderPath
                = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}transfer";

            return new ServerSettings
            {
                LogLevel = LogLevel.Info,
                TransferRetryLimit = 3,
                RetryLimitLockout = TimeSpan.FromMinutes(10),
                LocalServerFolderPath = defaultTransferFolderPath,
                TransferUpdateInterval = 0.0025f,
                FileTransferStalledTimeout = TimeSpan.FromSeconds(5),
                LocalNetworkCidrIp = string.Empty,
                LocalServerPortNumber = 0,
                SocketSettings = new SocketSettings(),
                RemoteServers = new List<ServerInfo>()
            };
        }

        public static Result SaveToFile(ServerSettings settings, string filePath)
        {
            try
            {
                var serializer = XmlSerializer.FromTypes(new[] { typeof(ServerSettings) })[0];
                using (var writer = new StreamWriter(filePath))
                {
                    serializer.Serialize(writer, settings);
                }
            }
            catch (FileNotFoundException ex)
            {
                return Result.Fail(ex.GetReport());
            }
            catch (Exception ex)
            {
                return Result.Fail(ex.GetReport());
            }

            return Result.Ok();
        }

        public static Result<ServerSettings> ReadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                var error =
                    $"The settings file does not exist at the specified location:{Environment.NewLine}{filePath}";

                Result.Fail<ServerSettings>(error);
            }

            var deserializeResult = Deserialize(filePath);
            if (deserializeResult.Failure)
            {
                return Result.Fail<ServerSettings>(deserializeResult.Error);
            }

            var settings = deserializeResult.Value;

            foreach (var server in settings.RemoteServers)
            {
                server.InitializeIpAddresses();
            }

            return Result.Ok(settings);
        }

        static Result<ServerSettings> Deserialize(string filePath)
        {
            ServerSettings settings;
            try
            {
                var deserializer = XmlSerializer.FromTypes(new[] {typeof(ServerSettings)})[0];
                using (var reader = new StreamReader(filePath))
                {
                    settings = (ServerSettings) deserializer.Deserialize(reader);
                }
            }
            catch (InvalidOperationException ex)
            {
                return Result.Fail<ServerSettings>(ex.GetReport());
            }
            catch (Exception ex)
            {
                return Result.Fail<ServerSettings>(ex.GetReport());
            }

            return Result.Ok(settings);
        }
    }
}
