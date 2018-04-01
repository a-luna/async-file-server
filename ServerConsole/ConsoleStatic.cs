namespace ServerConsole
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading.Tasks;

    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    using TplSockets;
    
    static class ConsoleStatic
    {
        public const string NoClientSelectedError = "Please select a remote server before choosing an action to perform.";

        const string PropmptMultipleLocalIPv4Addresses = "There are multiple IPv4 addresses available on this machine, choose the most appropriate local address:";
        const string NotifyLanTrafficOnly = "Unable to determine public IP address, this server will only be able to communicate with machines in the same local network.";
        const string IpChoiceClient = "\nWhich IP address would you like to use for this request?";

        public const int OneHalfSecondInMilliseconds = 500;

        internal const int PortRangeMin = 49152;
        internal const int PortRangeMax = 65535;

        const int LocalIpAddress = 1;
        const int PublicIpAddress = 2;

        public static AppSettings InitializeAppSettings(string settingsFilePath)
        {
            var defaultTransferFolderPath
                = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}transfer";

            var settings = new AppSettings
            {
                MaxDownloadAttempts = 3,
                TransferFolderPath = defaultTransferFolderPath,
                TransferUpdateInterval = 0.0025f
            };

            if (!File.Exists(settingsFilePath)) return settings;

            var deserialized = AppSettings.Deserialize(settingsFilePath);
            if (deserialized.Success)
            {
                settings = deserialized.Value;
            }
            else
            {
                Console.WriteLine(deserialized.Error);
            }

            return settings;
        }

        public static int GetPortNumberFromUser(string prompt, bool allowRandom)
        {
            var portNumber = 0;
            while (portNumber is 0)
            {
                Console.Clear();
                Console.WriteLine($"{prompt} (range {PortRangeMin}-{PortRangeMax}):");

                if (allowRandom)
                {
                    Console.WriteLine("Enter zero to use a random port number");
                }
                
                var input = Console.ReadLine() ?? string.Empty;
                if (input.Equals("zero") || input.Equals("0"))
                {
                    if (!allowRandom)
                    {
                        Console.WriteLine("0 (zero) is not within the allowed range, please try again.");
                        continue;
                    }

                    var rnd = new Random();
                    portNumber = rnd.Next(PortRangeMin, PortRangeMax + 1);
                    Console.WriteLine($"Your randomly chosen port number is: {portNumber}");
                    break;
                }

                var portValidationResult = ValidateNumberIsWithinRange(input, PortRangeMin, PortRangeMax);
                if (portValidationResult.Failure)
                {
                    Console.WriteLine(portValidationResult.Error);
                    continue;
                }

                portNumber = portValidationResult.Value;
            }

            return portNumber;
        }

        public static IPAddress GetLocalIpAddress()
        {
            var ipRequest = Network.GetLocalIPv4AddressFromInternet();
            if (ipRequest.Success)
            {
                return ipRequest.Value;
            }

            var localIps = Network.GetLocalIPv4AddressList();
            if (localIps.Count == 0)
            {
                return IPAddress.Any;
            }

            if (localIps.Count == 1)
            {
                return localIps.Find(ip => ip.AddressFamily == AddressFamily.InterNetwork);
            }

            var ipChoice = 0;
            var totalMenuChoices = localIps.Count;
            while (ipChoice == 0)
            {
                Console.Clear();
                Console.WriteLine(PropmptMultipleLocalIPv4Addresses);

                foreach (var i in Enumerable.Range(0, localIps.Count))
                {
                    Console.WriteLine($"{i + 1}. {localIps[i]}");
                }

                var input = Console.ReadLine();
                var validationResult = ValidateNumberIsWithinRange(input, 1, totalMenuChoices);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                ipChoice = validationResult.Value;
            }

            return localIps[ipChoice - 1];
        }

        public static async Task<IPAddress> GetPublicIpAddressForLocalMachineAsync()
        {
            var retrievePublicIp =
                await Network.GetPublicIPv4AddressAsync().ConfigureAwait(false);

            var publicIp = IPAddress.None;
            if (retrievePublicIp.Failure)
            {
                Console.WriteLine(NotifyLanTrafficOnly);
            }
            else
            {
                publicIp = retrievePublicIp.Value;
            }

            return publicIp;
        }

        public static Result<int> ValidateNumberIsWithinRange(string input, int rangeMin, int rangeMax)
        {
            if (string.IsNullOrEmpty(input))
            {
                return Result.Fail<int>("Error! Input was null or empty string.");
            }

            if (!int.TryParse(input, out var parsedNum))
            {
                return Result.Fail<int>($"Unable to parse int value from input string: {input}");
            }

            if (parsedNum < rangeMin || parsedNum > rangeMax)
            {
                return Result.Fail<int>($"{parsedNum} is not within allowed range {rangeMin}-{rangeMax}");
            }

            return Result.Ok(parsedNum);
        }

        public static Result<RemoteServer> GetRemoteServerConnectionInfoFromUser()
        {
            var clientIpAddressIsValid = false;
            var userMenuChoice = 0;
            string input;

            var clientIp = IPAddress.None;
            var remoteServerInfo = new RemoteServer();

            Console.Clear();
            while (!clientIpAddressIsValid)
            {
                Console.WriteLine("Enter the client's IPv4 address:");
                input = Console.ReadLine();

                var ipValidationResult = ValidateIpV4Address(input);
                if (ipValidationResult.Failure)
                {
                    Console.WriteLine(ipValidationResult.Error);
                    continue;
                }

                var parseIp = Network.ParseSingleIPv4Address(ipValidationResult.Value);
                clientIp = parseIp.Value;

                remoteServerInfo.ConnectionInfo.SessionIpAddress = clientIp;
                clientIpAddressIsValid = true;
            }

            while (userMenuChoice == 0)
            {
                Console.WriteLine($"\nIs {clientIp} a local or public IP address?");
                Console.WriteLine("1. Local");
                Console.WriteLine("2. Public/External");
                input = Console.ReadLine();

                var ipTypeValidationResult = ValidateNumberIsWithinRange(input, 1, 2);
                if (ipTypeValidationResult.Failure)
                {
                    Console.WriteLine(ipTypeValidationResult.Error);
                    continue;
                }

                userMenuChoice = ipTypeValidationResult.Value;
            }

            switch (userMenuChoice)
            {
                case PublicIpAddress:
                    remoteServerInfo.ConnectionInfo.PublicIpAddress = clientIp;
                    break;

                case LocalIpAddress:
                    remoteServerInfo.ConnectionInfo.LocalIpAddress = clientIp;
                    break;
            }

            remoteServerInfo.ConnectionInfo.Port = GetPortNumberFromUser("\nEnter the client's port number:", false);

            return Result.Ok(remoteServerInfo);
        }

        public static Result<string> ValidateIpV4Address(string input)
        {
            var parseIpResult = Network.ParseSingleIPv4Address(input);
            if (parseIpResult.Failure)
            {
                return Result.Fail<string>($"Unable tp parse IPv4 address from input string: {parseIpResult.Error}");
            }

            return Result.Ok(parseIpResult.Value.ToString());
        }

        public static Result SetSessionIpAddress(ConnectionInfo remoteServer)
        {
            var ipChoice = 0;
            while (ipChoice == 0)
            {
                Console.WriteLine(IpChoiceClient);
                Console.WriteLine($"1. Local IP ({remoteServer.LocalIpString})");
                Console.WriteLine($"2. Public IP ({remoteServer.PublicIpString})");

                var input = Console.ReadLine();
                Console.WriteLine(string.Empty);

                var validationResult = ValidateNumberIsWithinRange(input, 1, 2);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                ipChoice = validationResult.Value;
            }

            var sessionIp = IPAddress.None;
            if (ipChoice == PublicIpAddress)
            {
                sessionIp = remoteServer.PublicIpAddress;
            }

            if (ipChoice == LocalIpAddress)
            {
                sessionIp = remoteServer.LocalIpAddress;
            }

            remoteServer.SessionIpAddress = sessionIp;

            return Result.Ok();
        }

        public static bool ClientAlreadyAdded(RemoteServer newClient, List<RemoteServer> clients)
        {
            var exists = false;
            foreach (var remoteServer in clients)
            {
                if (remoteServer.ConnectionInfo.IsEqualTo(newClient.ConnectionInfo))
                {
                    exists = true;
                    break;
                }
            }
            return exists;
        }

        public static RemoteServer GetRemoteServer(RemoteServer client, List<RemoteServer> clientList)
        {
            var match = new RemoteServer();
            foreach (var server in clientList)
            {
                if (server.ConnectionInfo.IsEqualTo(client.ConnectionInfo))
                {
                    match = server;
                    break;
                }
            }

            return match;
        }

        public static bool PromptUserYesOrNo(string prompt)
        {
            var shutdownChoice = 0;
            while (shutdownChoice is 0)
            {
                Console.WriteLine($"\n{prompt}");
                Console.WriteLine("1. Yes");
                Console.WriteLine("2. No");

                var input = Console.ReadLine();
                var validationResult = ConsoleStatic.ValidateNumberIsWithinRange(input, 1, 2);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                shutdownChoice = validationResult.Value;
            }

            return shutdownChoice == 1;
        }
    }
}
