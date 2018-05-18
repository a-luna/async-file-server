namespace ServerConsole.Commands.Menus
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    using ServerCommands;

    class SelectServerActionMenu : MenuLoop, ICommand
    {
        AppState _state;
        readonly Logger _log = new Logger(typeof(SelectServerActionMenu));

        public SelectServerActionMenu(AppState state)
        {
            ReturnToParent = false;
            ItemText = "All server actions";
            MenuText = $"\nServer is listening for incoming requests on port {state.LocalServer.Info.Port}" +
                       $"\nLocal IP:\t{state.LocalServer.Info.LocalIpAddress}" +
                       $"\nPublic IP:\t{state.LocalServer.Info.PublicIpAddress}" +
                       "\nPlease make a choice from the menu below:";

            var sendTextMessageCommand = new SendTextMessageCommand(state);
            var sendFileCommand = new SendFileCommand();
            var getFileCommand = new GetFileCommand();
            var returnToMainMenuCommand = new ReturnToParentCommand("Return to main menu", true);

            MenuOptions.Add(sendTextMessageCommand);
            MenuOptions.Add(sendFileCommand);
            MenuOptions.Add(getFileCommand);
            MenuOptions.Add(returnToMainMenuCommand);

            _state = state;
        }

        Task<Result> ICommand.ExecuteAsync()
        {
            return ExecuteAsync();
        }

        public new async Task<Result> ExecuteAsync()
        {
            if (!_state.ClientSelected)
            {
                ReturnToParent = false;
                const string logError =
                    "Error: User tried to perform a server action before selecting a " +
                    "remote server (SelectServerActionMenu.ExecuteAsync)";

                _log.Error(logError);
                return Result.Fail(Resources.Error_NoClientSelectedError);
            }

            var exit = false;
            Result result = null;

            while (!exit)
            {
                var userSelection = 0;
                while (userSelection == 0)
                {
                    Menu.DisplayMenu(MenuText, MenuOptions);
                    var input = Console.ReadLine();

                    var validationResult = Menu.ValidateUserInput(input, MenuOptions.Count);
                    if (validationResult.Failure)
                    {
                        Console.WriteLine(validationResult.Error);
                        continue;
                    }

                    userSelection = validationResult.Value;
                }

                var selectedOption = MenuOptions[userSelection - 1];
                result = await selectedOption.ExecuteAsync();
                exit = selectedOption.ReturnToParent;

                if (result.Success) continue;

                _log.Error($"Error: {result.Error} (SelectServerActionMenu.ExecuteAsync)");
                Console.WriteLine(result.Error);
                exit = true;
            }
            return result;
        }
    }
}
