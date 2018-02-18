namespace ServerConsole
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TplSocketServer;

    class Program
    {
        const string SavedClientsFileName = "clients.txt";

        const int PortRangeMin = 49152;
        const int PortRangeMax = 65535;
        const int ListenPort = 50815;

        static async Task Main()
        {
            var myInfo = new ServerInfo
            {
                IpAddress = IpAddressHelper.GetLocalIpV4Address().ToString(),
                Port = ListenPort,
                TransferFolder = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}transfer"
            };

            var getListOfClientsResult = GetListOfClientsFromFile(myInfo);
            if (getListOfClientsResult.Failure)
            {
                Console.WriteLine($"Unable to read client info from file, due to error:\n{getListOfClientsResult.Error}");
            }

            var listOfClients = getListOfClientsResult.Value;

            var cts = new CancellationTokenSource();
            var token = cts.Token;

            var server = new TplSocketServer();
            server.EventOccurred += HandleServerEvent;

            var runServerTask = Task.Run(() => server.RunServerAsync(myInfo.Port, token), token);

            var menuTask = await ChooseMainMenuOptionAsync(server, myInfo, listOfClients, token);

            var serverShutdownResult = await runServerTask;
        }

        private static Result<List<ServerInfo>> GetListOfClientsFromFile(ServerInfo myInfo)
        {
            var clientInfoFilePath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}{SavedClientsFileName}";
            string[] fileLines = null;            

            try
            {
                if (File.Exists(clientInfoFilePath))
                {
                    fileLines = File.ReadAllLines(clientInfoFilePath);
                }
            }
            catch (IOException ex)
            {
                return Result.Fail<List<ServerInfo>>($"{ex.Message} ({ex.GetType()} raised in method GetListOfClientsFromFile)");
            }

            if (fileLines == null || fileLines.Length == 0)
            {
                return Result.Fail<List<ServerInfo>>("Error reading endpoints from file.");
            }

            var listOfClients = new List<ServerInfo>();
            foreach (var line in fileLines)
            {
                var parseClientInfoResult = ServerInfo.GetServerInfo(line);
                if (parseClientInfoResult.Failure)
                {
                    return Result.Fail<List<ServerInfo>>($"Unable to parse server connection info from file string: {line}");
                }

                var clientInfo = parseClientInfoResult.Value;
                if (!myInfo.IsEqualTo(clientInfo))
                {
                    listOfClients.Add(parseClientInfoResult.Value);
                }
            }

            return Result.Ok(listOfClients);
        }

        private static async Task<Result> ChooseMainMenuOptionAsync(TplSocketServer server, ServerInfo myInfo, List<ServerInfo> listOfClients, CancellationToken token)
        {
            var mainMenuChoice = 0;
            while (mainMenuChoice == 0)
            {
                Console.WriteLine($"Server is ready to handle incoming requests. My endpoint: {myInfo.GetEndPoint()}");
                Console.WriteLine("Please make a choice from the menu below:");
                Console.WriteLine("1. Send Text Message");
                Console.WriteLine("2. Send File");
                Console.WriteLine("3. Get File");
                Console.WriteLine("4. Shutdown");

                var input = Console.ReadLine();
                var validationResult = ValidateNumberIsWithinRange(input, 1, 4);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                mainMenuChoice = validationResult.Value;

                if (mainMenuChoice == 4)
                {
                    break;
                }

                var chooseClientResult = ChooseClient(listOfClients);
                if (chooseClientResult.Failure)
                {
                    mainMenuChoice = 0;
                    continue;
                }

                var chosenClient = chooseClientResult.Value;

                Result result = Result.Ok();
                switch (mainMenuChoice)
                {
                    case 1:
                        result = await SendTextMessageToClientAsync(server, myInfo, chosenClient, token);
                        break;

                    case 2:
                        break;

                    case 3:
                        break;
                        
                }

                if (result.Failure)
                {
                    return result;
                }
            }
            
            return Result.Ok();
        }

        private static async Task<Result> SendTextMessageToClientAsync(TplSocketServer server, ServerInfo myInfo, ServerInfo clientInfo, CancellationToken token)
        {
            Console.WriteLine($"Please enter a text message to send to {clientInfo.GetEndPoint()}");
            var message = Console.ReadLine();

            var sendMessageResult =
                await server.SendTextMessageAsync(
                    message,
                    clientInfo.IpAddress,
                    clientInfo.Port,
                    myInfo.IpAddress,
                    myInfo.Port,
                    token);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            return Result.Ok();
        }

        private static Result<ServerInfo> ChooseClient(List<ServerInfo> listOfClients)
        {
            int clientMenuChoice = 0;
            var totalMenuChoices = listOfClients.Count + 2;
            var addNewClient = listOfClients.Count + 1;
            var returnToMainMenu = totalMenuChoices;

            while (clientMenuChoice == 0)
            {
                Console.WriteLine("Choose a remote server for this request:");

                foreach (var i in Enumerable.Range(0, listOfClients.Count))
                {
                    Console.WriteLine($"{i + 1}. {listOfClients[i].IpAddress}:{listOfClients[i].Port}");
                }                

                Console.WriteLine($"{addNewClient}. Add New Client");
                Console.WriteLine($"{returnToMainMenu}. Return to Main Menu");

                var input = Console.ReadLine();
                var validationResult = ValidateInputClientMenu(input, totalMenuChoices);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                clientMenuChoice = validationResult.Value;
            }

            if (clientMenuChoice == returnToMainMenu)
            {
                // User chooses to return to main menu, we will not display an
                // error on the console in this case
                return Result.Fail<ServerInfo>(string.Empty);
            }

            if (clientMenuChoice == addNewClient)
            {
                ServerInfo newClientInfo = new ServerInfo();
                var clientInfoIsValid = false;

                while (!clientInfoIsValid)
                {
                    var addClientResult = AddNewClient();
                    if (addClientResult.Failure)
                    {
                        Console.WriteLine(addClientResult.Error);
                        continue;
                    }

                    newClientInfo = addClientResult.Value;
                    clientInfoIsValid = true;
                }

                AddNewClientToFile(newClientInfo);

                return Result.Ok(newClientInfo);
            }

            var chosenClient = listOfClients[clientMenuChoice - 1];

            return Result.Ok(chosenClient);
        }

        private static Result<ServerInfo> AddNewClient()
        {
            Console.WriteLine("Enter a valid IPv4 Address:");
            var input = Console.ReadLine();

            var ipValidationResult = ValidateIpV4Address(input);
            if (ipValidationResult.Failure)
            {
                return Result.Fail<ServerInfo>(ipValidationResult.Error);
            }

            var clientIp = ipValidationResult.Value;

            Console.WriteLine($"Enter a port number in range {PortRangeMin}-{PortRangeMax}:");
            input = Console.ReadLine();

            var portValidationResult = ValidateNumberIsWithinRange(input, PortRangeMin, PortRangeMax);
            if (ipValidationResult.Failure)
            {
                return Result.Fail<ServerInfo>(ipValidationResult.Error);
            }

            var clientPort = portValidationResult.Value;

            Console.WriteLine("Enter the path of the transfer folder on the client machine:");
            input = Console.ReadLine();

            var clientInfo = new ServerInfo
            {
                IpAddress = clientIp,
                Port = clientPort,
                TransferFolder = input
            };

            return Result.Ok(clientInfo);
        }

        private static void AddNewClientToFile(ServerInfo serverInfo)
        {
            using (StreamWriter sw = File.AppendText($"{Directory.GetCurrentDirectory()}\\{SavedClientsFileName}"))
            {
                sw.WriteLine($"{serverInfo.IpAddress}*{serverInfo.Port}*{serverInfo.TransferFolder}");
            }
        }

        private static Result<int> ValidateInputClientMenu(string input, int max)
        {
            if (string.IsNullOrEmpty(input))
            {
                return Result.Fail<int>("Invalid selection. Input was null or empty string.");
            }

            if (!int.TryParse(input, out int parsedInt))
            {
                return Result.Fail<int>($"Invalid selection. Unable to parse int value from input string: {input}");
            }

            if ((parsedInt <= 0) || (parsedInt > max))
            {
                return Result.Fail<int>($"Invalid selection. Value entered was not within allowed range (1-{max + 1}): {parsedInt}");
            }

            return Result.Ok(parsedInt);
        }

        private static Result<string> ValidateIpV4Address(string input)
        {
            var parseIpResult = input.GetSingleIpv4AddressFromString();
            if (parseIpResult.Failure)
            {
                return Result.Fail<string>($"Unable tp parse IPv4 address from input string: {parseIpResult.Error}");
            }

            return Result.Ok(parseIpResult.Value);
        }

        private static Result<int> ValidateNumberIsWithinRange(string input, int rangeMin, int rangeMax)
        {
            if (!int.TryParse(input, out int parsedNum))
            {
                return Result.Fail<int>($"Unable to parse int value from input string: {input}");
            }

            if (parsedNum < rangeMin || parsedNum > rangeMax)
            {
                return Result.Fail<int>($"{parsedNum} is not in allowed range {PortRangeMin}-{PortRangeMax}");
            }

            return Result.Ok(parsedNum);
        }

        private static void HandleServerEvent(ServerEventInfo serverevent)
        {
            //Console.WriteLine(serverevent.Report());

            switch (serverevent.EventType)
            {
                case ServerEventType.ListenOnLocalPortCompleted:
                    break;

                case ServerEventType.ReceiveTextMessageCompleted:
                    Console.WriteLine($"Message received from client {serverevent.RemoteServerIpAddress}:{serverevent.RemoteServerPortNumber}:");
                    Console.WriteLine(serverevent.TextMessage);
                    break;

                case ServerEventType.ReceiveFileBytesCompleted:
                    break;

                case ServerEventType.ReceiveConfirmationMessageCompleted:
                    break;

                case ServerEventType.ShutdownListenSocketCompleted:
                    break;

                case ServerEventType.ErrorOccurred:
                    break;
            }
        }
    }
}
