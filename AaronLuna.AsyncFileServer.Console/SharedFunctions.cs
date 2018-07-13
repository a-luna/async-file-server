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

        public static void DisplayLocalServerInfo(AppState state)
        {
            Console.Clear();
            Console.WriteLine(state.LocalServerInfo());
        }

        public static async Task<Result> SendTextMessageAsync(AppState state)
        {
            var ipAddress = state.SelectedServerInfo.SessionIpAddress;
            var port = state.SelectedServerInfo.PortNumber;

            Console.WriteLine($"{Environment.NewLine}Please enter a text message to send to {ipAddress}:{port}");
            var message = Console.ReadLine();

            var sendMessageResult =
                await state.LocalServer.SendTextMessageAsync(
                    message,
                    ipAddress,
                    port).ConfigureAwait(false);

            return sendMessageResult.Success
                ? Result.Ok()
                : sendMessageResult;
        }

        public static async Task<Result> RequestServerInfoAsync(
            AppState state,
            IPAddress ipAddress,
            int port)
        {
            state.WaitingForServerInfoResponse = true;

            var requestServerInfoResult =
                await state.LocalServer.RequestServerInfoAsync(
                        ipAddress,
                        port)
                    .ConfigureAwait(false);

            if (requestServerInfoResult.Failure)
            {
                var error =
                    "Error requesting additional info from remote server:" +
                    Environment.NewLine + requestServerInfoResult.Error;

                return Result.Fail(error);
            }

            while (state.WaitingForServerInfoResponse) { }

            return Result.Ok();
        }

        public static async Task<int> GetUserSelectionIndexAsync(
            string menuText,
            List<IMenuItem> menuItems,
            AppState state)
        {
            var userSelection = 0;
            while (userSelection == 0)
            {
                ConsoleMenu.DisplayMenu(menuText, menuItems);
                var input = Console.ReadLine();

                var validationResult = ValidateNumberIsWithinRange(input, 1, menuItems.Count);
                if (validationResult.Failure)
                {
                    Console.WriteLine(Environment.NewLine + validationResult.Error);
                    await Task.Delay(state.MessageDisplayTime).ConfigureAwait(false);

                    DisplayLocalServerInfo(state);
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
            var userSelection = await GetUserSelectionIndexAsync(menuText, menuItems, state).ConfigureAwait(false);
            return menuItems[userSelection - 1];
        }

        public static bool CidrIpHasChanged(AppState state)
        {
            var getCidrIp = NetworkUtilities.GetCidrIp();
            if (getCidrIp.Failure) return false;

            var newCidrIp = getCidrIp.Value;
            var cidrIpMatch = state.Settings.LocalNetworkCidrIp == newCidrIp;
            if (cidrIpMatch) return false;

            var prompt =
                "The current value for CIDR IP is " +
                $"{state.Settings.LocalNetworkCidrIp}, however it appears " +
                $"that {newCidrIp} is the correct value for the current LAN, " +
                "would you like to use this value?";

            var updateCidrIp = PromptUserYesOrNo(prompt);
            if (!updateCidrIp) return false;

            state.Settings.LocalNetworkCidrIp = newCidrIp;
            return true;
        }

        public static string InitializeLanCidrIp()
        {
            var getCidrIp = NetworkUtilities.GetCidrIp();
            if (getCidrIp.Failure)
            {
                return GetCidrIpFromUser();
            }

            var cidrIp = getCidrIp.Value;
            var prompt = "Found a single IPv4 address assiciated with the only ethernet adapter " +
                         $"on this machine, is it ok to use {cidrIp} as the CIDR IP?";

            return PromptUserYesOrNo(prompt)
                ? cidrIp
                : GetCidrIpFromUser();
        }

        public static string GetCidrIpFromUser()
        {
            var cidrIp = GetIpAddressFromUser(Resources.Prompt_GetCidrIp);
            var cidrNetworkBitCount = GetCidrIpNetworkBitCountFromUser();
            return $"{cidrIp}/{cidrNetworkBitCount}";
        }

        public static int GetCidrIpNetworkBitCountFromUser()
        {
            var bitCount = 0;
            while (bitCount is 0)
            {
                var prompt =
                    $"{Environment.NewLine}{Resources.Prompt_GetCidrIpNetworkBitCount} " +
                    $"(range {CidrPrefixBitsCountMin}-{CidrPrefixBitsCountMax}):";

                Console.WriteLine(prompt);

                var input = Console.ReadLine();
                var bitCountValidationResult = ValidateNumberIsWithinRange(input, CidrPrefixBitsCountMin, CidrPrefixBitsCountMax);
                if (bitCountValidationResult.Failure)
                {
                    Console.WriteLine(bitCountValidationResult.Error);
                    continue;
                }

                bitCount = bitCountValidationResult.Value;
            }

            return bitCount;
        }

        public static IPAddress GetIpAddressFromUser(string prompt)
        {
            var ipAddress = IPAddress.None;

            while (ipAddress.Equals(IPAddress.None))
            {
                Console.WriteLine($"{Environment.NewLine}{prompt}");
                var input = Console.ReadLine();

                var parseIpResult = NetworkUtilities.ParseSingleIPv4Address(input);
                if (parseIpResult.Failure)
                {
                    var errorMessage = $"Unable tp parse IPv4 address from input string: {parseIpResult.Error}";
                    Console.WriteLine(errorMessage);
                    continue;
                }

                ipAddress = parseIpResult.Value;
            }

            return ipAddress;
        }

        public static int GetPortNumberFromUser(string prompt, bool allowRandom)
        {
            var portNumber = 0;
            while (portNumber is 0)
            {
                Console.WriteLine($"{Environment.NewLine}{prompt} (range {PortRangeMin}-{PortRangeMax}):");

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

        public static Result<ServerInfo> GetRemoteServer(
            ServerInfo lookup,
            List<ServerInfo> serverInfoList)
        {
            foreach (var server in serverInfoList)
            {
                if (!server.IsEqualTo(lookup)) continue;
                return Result.Ok(server);
            }

            return Result.Fail<ServerInfo>("No server was found matching the details provided");
        }

        public static bool ServerInfoAlreadyExists(
            ServerInfo lookup,
            List<ServerInfo> serverInfoList)
        {
            var exists = false;
            foreach (var remoteServer in serverInfoList)
            {
                if (!remoteServer.IsEqualTo(lookup)) continue;
                exists = true;
                break;
            }
            return exists;
        }

        public static string SetSelectedServerName(ServerInfo serverInfo)
        {
            var defaultServerName = $"{serverInfo.SessionIpAddress}:{serverInfo.PortNumber}-{serverInfo.Platform}";

            var initialPrompt =
                $"Would you like to enter a name to help you identify this server? If you select no, a default name will be created ({defaultServerName})";

            var enterCustomName = PromptUserYesOrNo(initialPrompt);

            return enterCustomName
                ? GetServerNameFromUser(Resources.Prompt_SetRemoteServerName)
                : defaultServerName;
        }

        public static string GetServerNameFromUser(string prompt)
        {
            var remoteServerName = string.Empty;
            while (string.IsNullOrEmpty(remoteServerName))
            {
                Console.WriteLine(Environment.NewLine + prompt);
                var input = Console.ReadLine();

                var confirm = $"Is \"{input}\" the name you wish to use for this server? Select no if you would like to change this value.";

                var useThisName = PromptUserYesOrNo(confirm);
                if (useThisName)
                {
                    remoteServerName = input;
                }
            }

            return remoteServerName;
        }

        public static Result<string> LookupRemoteServerName(ServerInfo lookup, List<ServerInfo> serverInfoList)
        {
            var findMatch = GetRemoteServer(lookup, serverInfoList);
            if (findMatch.Failure)
            {
                return Result.Fail<string>(findMatch.Error);
            }

            return Result.Ok(findMatch.Value.Name);
        }

        public static bool PromptUserYesOrNo(string prompt)
        {
            var userChoice = 0;
            while (userChoice is 0)
            {
                Console.WriteLine(Environment.NewLine + prompt);
                Console.WriteLine("1. Yes");
                Console.WriteLine("2. No");

                var input = Console.ReadLine();
                var validationResult = ValidateNumberIsWithinRange(input, 1, 2);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                userChoice = validationResult.Value;
            }

            return userChoice == 1;
        }
    }
}
