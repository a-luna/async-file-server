namespace ServerConsole
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;

    using AaronLuna.Common.IO;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    using TplSocketServer;

    public static class ConsoleStatic
    {
        const string IpChoiceClient = "\nWhich IP address would you like to use for this request?";

        const int PortRangeMin = 49152;
        const int PortRangeMax = 65535;

        const int LocalIpAddress = 1;
        const int PublicIpAddress = 2;

        const string PropmptMultipleLocalIPv4Addresses = "There are multiple IPv4 addresses available on this machine, choose the most appropriate local address:";

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

                var input = Console.ReadLine();

                if (string.IsNullOrEmpty(input))
                {
                    Console.WriteLine("User input was null or empty string, please try again.");
                    continue;
                }

                if (input.Equals("zero") || input.Equals("0"))
                {
                    if (!allowRandom)
                    {
                        Console.WriteLine("Zero is not within the allowed range, please try again.");
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

        public static IPAddress GetLocalIpToBindTo()
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

        public static Result<RemoteServer> GetRemoteServerConnectionInfoFromUser()
        {
            var clientIpAddressIsValid = false;
            var userMenuChoice = 0;
            var input = string.Empty;

            var clientIp = IPAddress.None;
            var remoteServerInfo = new RemoteServer();

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
            
            remoteServerInfo.ConnectionInfo.Port =
                GetPortNumberFromUser("\nEnter the client's port number:", false);

            return Result.Ok(remoteServerInfo);
        }

        public static Result SetSessionIpAddress(RemoteServer remoteServer)
        {
            var ipChoice = 0;
            while (ipChoice == 0)
            {
                Console.WriteLine(IpChoiceClient);
                Console.WriteLine($"1. Local IP ({remoteServer.ConnectionInfo.LocalIpString})");
                Console.WriteLine($"2. Public IP ({remoteServer.ConnectionInfo.PublicIpString})");

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
                sessionIp = remoteServer.ConnectionInfo.PublicIpAddress;
            }

            if (ipChoice == LocalIpAddress)
            {
                sessionIp = remoteServer.ConnectionInfo.LocalIpAddress;
            }

            remoteServer.ConnectionInfo.SessionIpAddress = sessionIp;

            return Result.Ok();
        }

        public static Result<string> ChooseFileToSend(string transferFolderPath)
        {
            List<string> listOfFiles;
            try
            {
                listOfFiles = Directory.GetFiles(transferFolderPath).ToList();
            }
            catch (IOException ex)
            {
                return Result.Fail<string>($"{ex.Message} ({ex.GetType()})");
            }

            if (listOfFiles.Count == 0)
            {
                return Result.Fail<string>(
                    $"Transfer folder is empty, please place files in the path below:\n{transferFolderPath}\n\nReturning to main menu...");
            }

            var fileMenuChoice = 0;
            var totalMenuChoices = listOfFiles.Count + 1;
            var returnToPreviousMenu = totalMenuChoices;

            while (fileMenuChoice == 0)
            {
                Console.WriteLine("Choose a file to send:");

                foreach (var i in Enumerable.Range(0, listOfFiles.Count))
                {
                    var fileName = Path.GetFileName(listOfFiles[i]);
                    var fileSize = new FileInfo(listOfFiles[i]).Length;
                    Console.WriteLine($"{i + 1}. {fileName} ({FileHelper.FileSizeToString(fileSize)})");
                }

                Console.WriteLine($"{returnToPreviousMenu}. Return to Previous Menu");

                var input = Console.ReadLine();

                var validationResult = ValidateNumberIsWithinRange(input, 1, totalMenuChoices);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                fileMenuChoice = validationResult.Value;
            }

            if (fileMenuChoice == returnToPreviousMenu)
            {
                return Result.Fail<string>("Returning to main menu");
            }

            return Result.Ok(listOfFiles[fileMenuChoice - 1]);
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
                var validationResult = ValidateNumberIsWithinRange(input, 1, 2);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                shutdownChoice = validationResult.Value;
            }

            return shutdownChoice == 1;
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
