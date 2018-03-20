using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using AaronLuna.Common.Network;

namespace ServerConsole
{
    using System;
    using System.IO;

    using TplSocketServer;

    using AaronLuna.Common.Result;

    static class ConsoleStatic
    {
        const string PropmptMultipleLocalIPv4Addresses = "There are multiple IPv4 addresses available on this machine, choose the most appropriate local address:";
        const string NotifyLanTrafficOnly = "Unable to determine public IP address, this server will only be able to communicate with machines in the same local network.";

        internal const int PortRangeMin = 49152;
        internal const int PortRangeMax = 65535;

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
    }
}
