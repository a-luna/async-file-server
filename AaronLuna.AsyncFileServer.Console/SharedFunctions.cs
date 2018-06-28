namespace AaronLuna.AsyncFileServer.Console
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Threading.Tasks;

    using Model;
    using Common.Console.Menu;
    using Common.Network;
    using Common.Result;

    static class SharedFunctions
    {
        public const int PortRangeMin = 49152;
        public const int PortRangeMax = 65535;

        public const int CidrPrefixBitsCountMin = 0;
        public const int CidrPrefixBitsCountMax = 32;

        public static async Task<int> GetUserSelectionIndexAsync(
            string menuText,
            List<IMenuItem> menuItems,
            AppState state)
        {
            var userSelection = 0;
            while (userSelection == 0)
            {
                Menu.DisplayMenu(menuText, menuItems);
                var input = System.Console.ReadLine();

                var validationResult = ValidateNumberIsWithinRange(input, 1, menuItems.Count);
                if (validationResult.Failure)
                {
                    System.Console.WriteLine(Environment.NewLine + validationResult.Error);
                    await Task.Delay(state.MessageDisplayTime);

                    state.DisplayCurrentStatus();
                    continue;
                }

                userSelection = validationResult.Value;
            }

            return userSelection;
        }

        public static async Task<IMenuItem> GetUserSelectionAsync(
            string menuText,
            List<IMenuItem> menuItems,
            AppState state)
        {
            var userSelection = await GetUserSelectionIndexAsync(menuText, menuItems, state);
            return menuItems[userSelection - 1];
        }

        public static string InitializeLanCidrIp()
        {
            var getCidrIp = NetworkUtilities.AttemptToDetermineLanCidrIp();
            if (getCidrIp.Failure)
            {
                return GetCidrIpFromUser();
            }

            var cidrIp = getCidrIp.Value;
            var prompt = "Found a single IPv4 address assiciated with the only ethernet adapter " +
                         $"on this machine, is it ok to use {cidrIp} as the CIDR IP?";

            return PromptUserYesOrNo(prompt) ? cidrIp : GetCidrIpFromUser();
        }

        public static string GetCidrIpFromUser()
        {
            var cidrIp = GetIpAddressFromUser(Resources.Prompt_SetLanCidrIp);
            var cidrNetworkBitCount = GetCidrIpNetworkBitCountFromUser();
            return $"{cidrIp}/{cidrNetworkBitCount}";
        }

        public static int GetCidrIpNetworkBitCountFromUser()
        {
            const string prompt = "Enter the number of bits used to identify the network portion " +
                                  "of an IP address on your local network (i.e., enter the value " +
                                  "of 'n' in CIDR notation a.b.c.d/n)";

            var bitCount = 0;
            while (bitCount is 0)
            {
                //Console.Clear();
                System.Console.WriteLine($"{Environment.NewLine}{prompt} (range {CidrPrefixBitsCountMin}-{CidrPrefixBitsCountMax}):");

                var input = System.Console.ReadLine();
                var bitCountValidationResult = ValidateNumberIsWithinRange(input, CidrPrefixBitsCountMin, CidrPrefixBitsCountMax);
                if (bitCountValidationResult.Failure)
                {
                    System.Console.WriteLine(bitCountValidationResult.Error);
                    continue;
                }

                bitCount = bitCountValidationResult.Value;
            }

            return bitCount;
        }

        public static int GetPortNumberFromUser(string prompt, bool allowRandom)
        {
            var portNumber = 0;
            while (portNumber is 0)
            {
                //Console.Clear();
                System.Console.WriteLine($"{Environment.NewLine}{prompt} (range {PortRangeMin}-{PortRangeMax}):");

                if (allowRandom)
                {
                    System.Console.WriteLine("Enter zero to use a random port number");
                }

                var input = System.Console.ReadLine() ?? string.Empty;
                if (input.Equals("zero") || input.Equals("0"))
                {
                    if (!allowRandom)
                    {
                        System.Console.WriteLine("0 (zero) is not within the allowed range, please try again.");
                        continue;
                    }

                    var rnd = new Random();
                    portNumber = rnd.Next(PortRangeMin, PortRangeMax + 1);
                    System.Console.WriteLine($"Your randomly chosen port number is: {portNumber}");
                    break;
                }

                var portValidationResult = ValidateNumberIsWithinRange(input, PortRangeMin, PortRangeMax);
                if (portValidationResult.Failure)
                {
                    System.Console.WriteLine(portValidationResult.Error);
                    continue;
                }

                portNumber = portValidationResult.Value;
            }

            return portNumber;
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

            if (rangeMin > parsedNum || parsedNum > rangeMax)
            {
                return Result.Fail<int>($"{parsedNum} is not within allowed range {rangeMin}-{rangeMax}");
            }

            return Result.Ok(parsedNum);
        }

        public static IPAddress GetIpAddressFromUser(string prompt)
        {
            var ipAddress = IPAddress.None;
            //Console.Clear();

            while (ipAddress.Equals(IPAddress.None))
            {
                System.Console.WriteLine($"{Environment.NewLine}{prompt}");
                var input = System.Console.ReadLine();

                var parseIpResult = NetworkUtilities.ParseSingleIPv4Address(input);
                if (parseIpResult.Failure)
                {
                    var errorMessage = $"Unable tp parse IPv4 address from input string: {parseIpResult.Error}";
                    System.Console.WriteLine(errorMessage);
                    continue;
                }

                ipAddress = parseIpResult.Value;
            }

            return ipAddress;
        }

        public static bool ServerInfoAlreadyExists(ServerInfo newClient, List<ServerInfo> clients)
        {
            var exists = false;
            foreach (var remoteServer in clients)
            {
                if (remoteServer.IsEqualTo(newClient))
                {
                    exists = true;
                    break;
                }
            }
            return exists;
        }

        public static ServerInfo GetRemoteServer(ServerInfo client, List<ServerInfo> clientList)
        {
            var match = new ServerInfo();
            foreach (var server in clientList)
            {
                if (!server.IsEqualTo(client)) continue;
                match = server;
                break;
            }

            return match;
        }

        public static bool PromptUserYesOrNo(string prompt)
        {
            var userChoice = 0;
            while (userChoice is 0)
            {
                System.Console.WriteLine($"\n{prompt}");
                System.Console.WriteLine("1. Yes");
                System.Console.WriteLine("2. No");

                var input = System.Console.ReadLine();
                var validationResult = ValidateNumberIsWithinRange(input, 1, 2);
                if (validationResult.Failure)
                {
                    System.Console.WriteLine(validationResult.Error);
                    continue;
                }

                userChoice = validationResult.Value;
            }

            return userChoice == 1;
        }
    }
}
