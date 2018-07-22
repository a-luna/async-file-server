namespace AaronLuna.AsyncFileServer.Console
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Threading.Tasks;

    using Model;
    using Common.Console.Menu;
    using Common.Extensions;
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

        public static void NotifyUserErrorOccurred(string error)
        {
            Console.WriteLine(error + Environment.NewLine);
            Console.WriteLine("Press enter to return to the previous menu.");
            Console.ReadLine();
        }

        public static Task<Result> SendTextMessageAsync(AppState state, ServerInfo remoteServerInfo)
        {
            var ipAddress = remoteServerInfo.SessionIpAddress;
            var port = remoteServerInfo.PortNumber;

            Console.Clear();
            DisplayLocalServerInfo(state);
            Console.WriteLine($"Please enter a text message to send to {ipAddress}:{port}");
            var message = Console.ReadLine();
            Console.WriteLine(string.Empty);

            return state.LocalServer.SendTextMessageAsync(
                    message,
                    ipAddress,
                    port);
        }

        public static async Task<Result> RequestServerInfoAsync(
            AppState state,
            ServerInfo serverInfo,
            bool promptUserForServerName)
        {
            var ipAddress = serverInfo.SessionIpAddress;
            var port = serverInfo.PortNumber;
            var timeout = state.Settings.SocketSettings.SocketTimeoutInMilliseconds;

            DisplayLocalServerInfo(state);
            Console.WriteLine("Requesting additional info from remote server...");

            var requestServerInfoTask =
                Task.Run(() => RequestServerInfoTaskAsync(state, ipAddress, port));

            if (requestServerInfoTask == await Task.WhenAny(requestServerInfoTask, Task.Delay(timeout)))
            {
                var requestServerInfo = await requestServerInfoTask;
                if (requestServerInfo.Failure)
                {
                    return requestServerInfo;
                }

                DisplayLocalServerInfo(state);

                if (promptUserForServerName)
                {
                    serverInfo.Name = SetSelectedServerName(state, serverInfo);
                }

                state.Settings.RemoteServers.Add(serverInfo);

                var saveSettings = state.SaveSettingsToFile();
                if (saveSettings.Failure)
                {
                    return saveSettings;
                }
            }
            else
            {
                return Result.Fail(Resources.Error_ServerInfoRequestTimedout);
            }

            return Result.Ok();
        }

        static async Task<Result> RequestServerInfoTaskAsync(
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

        public static int GetUserSelectionIndex(string menuText, List<IMenuItem> menuItems, AppState state)
        {
            var userSelection = 0;
            while (userSelection == 0)
            {
                ConsoleMenu.DisplayMenu(menuText, menuItems);
                var input = Console.ReadLine();

                var inputValidation = ValidateNumberIsWithinRange(input, 1, menuItems.Count);
                if (inputValidation.Failure)
                {
                    NotifyUserErrorOccurred(inputValidation.Error);
                    DisplayLocalServerInfo(state);
                    continue;
                }

                userSelection = inputValidation.Value;
            }

            return userSelection;
        }

        public static IMenuItem GetUserSelection(string menuText, List<IMenuItem> menuItems, AppState state)
        {
            var userSelection = GetUserSelectionIndex(menuText, menuItems, state);
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

            var updateCidrIp = PromptUserYesOrNo(state, prompt);
            if (!updateCidrIp) return false;

            state.Settings.LocalNetworkCidrIp = newCidrIp;
            return true;
        }

        public static string InitializeLanCidrIp(AppState state)
        {
            var getCidrIp = NetworkUtilities.GetCidrIp();
            if (getCidrIp.Failure)
            {
                return GetCidrIpFromUser();
            }

            var cidrIp = getCidrIp.Value;
            var prompt = "Found a single IPv4 address assiciated with the only ethernet adapter " +
                         $"on this machine, is it ok to use {cidrIp} as the CIDR IP?";

            return PromptUserYesOrNo(state, prompt)
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

                var inputValidation = ValidateNumberIsWithinRange(input, CidrPrefixBitsCountMin, CidrPrefixBitsCountMax);
                if (inputValidation.Failure)
                {
                    NotifyUserErrorOccurred(inputValidation.Error);
                    continue;
                }

                bitCount = inputValidation.Value;
            }

            return bitCount;
        }

        public static IPAddress GetIpAddressFromUser(string prompt)
        {
            var ipAddress = IPAddress.None;

            while (ipAddress.Equals(IPAddress.None))
            {
                Console.WriteLine(prompt);
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

                var inputValidation = ValidateNumberIsWithinRange(input, PortRangeMin, PortRangeMax);
                if (inputValidation.Failure)
                {
                    NotifyUserErrorOccurred(inputValidation.Error);
                    continue;
                }

                portNumber = inputValidation.Value;
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

        public static string SetSelectedServerName(AppState state, ServerInfo serverInfo)
        {
            var defaultServerName = $"{serverInfo.SessionIpAddress}:{serverInfo.PortNumber}-{serverInfo.Platform}";

            var initialPrompt =
                $"Would you like to enter a name to help you identify this server? If you select no, a default name will be created ({defaultServerName})";

            var enterCustomName = PromptUserYesOrNo(state, initialPrompt);

            return enterCustomName
                ? GetServerNameFromUser(state, Resources.Prompt_SetRemoteServerName)
                : defaultServerName;
        }

        public static string GetServerNameFromUser(AppState state, string prompt)
        {
            var remoteServerName = string.Empty;
            while (string.IsNullOrEmpty(remoteServerName))
            {
                DisplayLocalServerInfo(state);
                Console.WriteLine(prompt);
                var input = Console.ReadLine();

                var nameIsValid = input.IsValidFileName();
                if (!nameIsValid)
                {
                    var error =
                        $"\"{input}\" contains invalid characters, only a-z, A-Z, 0-9 " +
                        "and \'.\' (period) \'_\' (underscore) \'-\' (hyphen) are allowed.";

                    NotifyUserErrorOccurred(error);
                    continue;
                }

                var confirm =
                    $"Is \"{input}\" the name you wish to use for this server? " +
                    "Select no if you would like to change this value.";

                var useThisName = PromptUserYesOrNo(state, confirm);
                if (useThisName)
                {
                    remoteServerName = input;
                }
            }

            return remoteServerName;
        }

        public static Result LookupRemoteServerName(ServerInfo lookup, List<ServerInfo> serverInfoList)
        {
            if (!string.IsNullOrEmpty(lookup.Name)) return Result.Ok();

            var findMatch = GetRemoteServer(lookup, serverInfoList);

            if (findMatch.Success)
            {
                lookup.Name = findMatch.Value.Name;
            }

            return findMatch;
        }

        public static bool PromptUserYesOrNo(AppState state, string prompt)
        {
            var userChoice = 0;
            while (userChoice is 0)
            {
                DisplayLocalServerInfo(state);

                Console.WriteLine(prompt);
                Console.WriteLine("1. Yes");
                Console.WriteLine("2. No");

                var input = Console.ReadLine();

                var inputValidation = ValidateNumberIsWithinRange(input, 1, 2);
                if (inputValidation.Failure)
                {
                    NotifyUserErrorOccurred(inputValidation.Error);
                    continue;
                }

                userChoice = inputValidation.Value;
            }

            return userChoice == 1;
        }
    }
}
