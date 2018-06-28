﻿namespace AaronLuna.AsyncFileServer.Console
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Xml;
    using System.Xml.Serialization;

    using Model;
    using Common.Network;
    using Common.Result;

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

                var saveSettings = SaveToFile(defaultSettings, filePath);
                if (saveSettings.Failure)
                {
                    return Result.Fail<ServerSettings>(saveSettings.Error);
                }

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
                var deserializer = XmlSerializer.FromTypes(new[] { typeof(ServerSettings) })[0];
                using (var reader = new StreamReader(filePath))
                {
                    settings = (ServerSettings)deserializer.Deserialize(reader);
                }
            }
            catch (FileNotFoundException ex)
            {
                return Result.Fail<ServerSettings>($"{ex.Message} ({ex.GetType()})");
            }
            catch (Exception ex)
            {
                return Result.Fail<ServerSettings>($"{ex.Message} ({ex.GetType()})");
            }

            return Result.Ok(settings);
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
                return Result.Fail($"{ex.Message} ({ex.GetType()})");
            }
            catch (Exception ex)
            {
                return Result.Fail($"{ex.Message} ({ex.GetType()})");
            }

            return Result.Ok();
        }

        public void InitializeIpAddresses()
        {
            foreach (var server in RemoteServers)
            {
                var localIp = server.LocalIpString;
                var publicIp = server.PublicIpString;
                var sessionIp = server.SessionIpString;

                server.LocalIpAddress = IPAddress.None;
                server.PublicIpAddress = IPAddress.None;
                server.SessionIpAddress = IPAddress.None;

                if (!string.IsNullOrEmpty(localIp))
                {
                    var parseLocalIpResult = NetworkUtilities.ParseSingleIPv4Address(localIp);
                    if (parseLocalIpResult.Success)
                    {
                        server.LocalIpAddress = parseLocalIpResult.Value;
                    }
                }
                
                if (!string.IsNullOrEmpty(publicIp))
                {
                    var parsePublicIpResult = NetworkUtilities.ParseSingleIPv4Address(publicIp);
                    if (parsePublicIpResult.Success)
                    {
                        server.PublicIpAddress = parsePublicIpResult.Value;
                    }
                }

                if (!string.IsNullOrEmpty(sessionIp))
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