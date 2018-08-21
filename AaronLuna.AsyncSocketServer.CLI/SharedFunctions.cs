using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.Network;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI
{
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

        public static void ModalMessage(string error, string prompt)
        {
            Console.WriteLine(error + Environment.NewLine);
            Console.WriteLine(prompt);
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

            return state.LocalServer.SendTextMessageAsync(remoteServerInfo, message);
        }

        public static Task<Result> RequestServerInfoAsync(AppState state,
            IPAddress remoteServerIpAddress,
            int remoteServerPort)
        {
            if (!state.PromptUserForServerName)
            {
                return RequestServerInfoTaskAsync(
                    state,
                    remoteServerIpAddress,
                    remoteServerPort);
            }

            DisplayLocalServerInfo(state);
            Console.WriteLine("Requesting additional info from remote server...");

            return RequestServerInfoTaskAsync(state, remoteServerIpAddress, remoteServerPort);
        }

        static async Task<Result> RequestServerInfoTaskAsync(AppState state, IPAddress ipAddress, int port)
        {
            state.WaitingForServerInfoResponse = true;
            var serverInfoResponseTimeout = false;

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

            var timeoutTask = Task.Run(ServerInfoResponseTimeoutTask);
            while (state.WaitingForServerInfoResponse)
            {
                if (serverInfoResponseTimeout) break;
            }

            await timeoutTask;

            if (!state.WaitingForServerInfoResponse)
            {
                Result.Fail(Resources.Error_ServerInfoRequestTimeout);
            }

            return Result.Ok();

            async Task ServerInfoResponseTimeoutTask()
            {
                await Task.Delay(Common.Constants.OneSecondInMilliseconds).ConfigureAwait(false);
                serverInfoResponseTimeout = true;
            }
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
                    ModalMessage(inputValidation.Error, Resources.Prompt_ReturnToPreviousMenu);
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
                return GetCidrIpFromUser(state);
            }

            var cidrIp = getCidrIp.Value;
            var prompt = "Found a single IPv4 address associated with the only ethernet adapter " +
                         $"on this machine, is it ok to use {cidrIp} as the CIDR IP?";

            return PromptUserYesOrNo(state, prompt)
                ? cidrIp
                : GetCidrIpFromUser(state);
        }

        public static string GetCidrIpFromUser(AppState state)
        {
            var cidrIp = GetIpAddressFromUser(state, Resources.Prompt_GetCidrIp);
            var cidrNetworkBitCount = GetCidrIpNetworkBitCountFromUser(state);
            return $"{cidrIp}/{cidrNetworkBitCount}";
        }

        public static int GetCidrIpNetworkBitCountFromUser(AppState state)
        {
            var bitCount = 0;
            while (bitCount is 0)
            {
                var prompt =
                    $"{Resources.Prompt_GetCidrIpNetworkBitCount} (range {CidrPrefixBitsCountMin}-{CidrPrefixBitsCountMax}):";

                DisplayLocalServerInfo(state);
                Console.WriteLine(prompt);

                var input = Console.ReadLine();
                var inputValidation = ValidateNumberIsWithinRange(input, CidrPrefixBitsCountMin, CidrPrefixBitsCountMax);

                if (inputValidation.Failure)
                {
                    ModalMessage(inputValidation.Error, Resources.Prompt_PressEnterToContinue);
                    continue;
                }

                bitCount = inputValidation.Value;
            }

            return bitCount;
        }

        public static IPAddress GetIpAddressFromUser(AppState state, string prompt)
        {
            var ipAddress = IPAddress.None;

            while (ipAddress.Equals(IPAddress.None))
            {
                DisplayLocalServerInfo(state);
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

        public static int GetPortNumberFromUser(AppState state, string prompt, bool allowRandom)
        {
            var portNumber = 0;
            while (portNumber is 0)
            {
                DisplayLocalServerInfo(state);
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

                    var randomPort = $"Your randomly chosen port number is: {portNumber}";
                    ModalMessage(randomPort, Resources.Prompt_PressEnterToContinue);

                    break;
                }

                var inputValidation = ValidateNumberIsWithinRange(input, PortRangeMin, PortRangeMax);
                if (inputValidation.Failure)
                {
                    ModalMessage(inputValidation.Error, Resources.Prompt_PressEnterToContinue);
                    continue;
                }

                portNumber = inputValidation.Value;
            }

            return portNumber;
        }

        public static Result<bool> UserEnteredAnything(string input)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrWhiteSpace(input))
            {
                return Result.Fail<bool>("Error! Input was null or empty string.");
            }

            return Result.Ok(true);
        }

        public static Result<int> ValidateNumberIsWithinRange(string input, int rangeMin, int rangeMax)
        {
            var userEnteredAnything = UserEnteredAnything(input);
            if (userEnteredAnything.Failure)
            {
                return Result.Fail<int>(userEnteredAnything.Error);
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

        public static bool CheckIfRemoteServerAlreadySaved(
            ServerInfo localServerInfo,
            IPAddress remoteServerIpAddress,
            int remoteServerPort,
            List<ServerInfo> serverInfoList)
        {
            var remoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            if (localServerInfo.IsEqualTo(remoteServerInfo)) return true;

            var exists = ServerInfoAlreadyExists(remoteServerInfo, serverInfoList);
            if (!exists) return false;

            LookupRemoteServerName(remoteServerInfo, serverInfoList);
            return true;
        }

        public static string SetSelectedServerName(AppState state, ServerInfo serverInfo)
        {
            var defaultServerName = $"{serverInfo.SessionIpAddress}:{serverInfo.PortNumber}-{serverInfo.Platform}";

            var initialPrompt =
                "Would you like to enter a name to help you identify this server? " +
                Environment.NewLine + Environment.NewLine +
                $"If you select no, a default name will be used ({defaultServerName}). " +
                "You can edit this name at any time.";

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

                var userEnteredAnything = UserEnteredAnything(input);
                if (userEnteredAnything.Failure)
                {
                   ModalMessage(userEnteredAnything.Error, Resources.Prompt_PressEnterToContinue);
                    continue;
                }

                var nameIsValid = input.IsValidFileName();
                if (!nameIsValid)
                {
                    var error =
                        $"\"{input}\" contains invalid characters, only a-z, A-Z, 0-9 " +
                        "and \'.\' (period) \'_\' (underscore) \'-\' (hyphen) are allowed.";

                    ModalMessage(error, Resources.Prompt_PressEnterToContinue);
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

                Console.WriteLine(prompt + Environment.NewLine);
                Console.WriteLine("1. Yes");
                Console.WriteLine("2. No");

                var input = Console.ReadLine();

                var inputValidation = ValidateNumberIsWithinRange(input, 1, 2);
                if (inputValidation.Failure)
                {
                    ModalMessage(inputValidation.Error, Resources.Prompt_PressEnterToContinue);
                    continue;
                }

                userChoice = inputValidation.Value;
            }

            return userChoice == 1;
        }
    }
}
