namespace ServerConsole.Commands.Menus
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    using ServerCommands;

    class SelectServerActionMenu : SelectionMenuLoop, ICommand
    {
        AppState _state;
        readonly Logger _log = new Logger(typeof(SelectServerActionMenu));

        public SelectServerActionMenu(AppState state)
        {
            _log.Info("Begin: Instantiate SelectServerActionMenu");

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

            Options.Add(sendTextMessageCommand);
            Options.Add(sendFileCommand);
            Options.Add(getFileCommand);
            Options.Add(returnToMainMenuCommand);

            _state = state;

            _log.Info("Complete: Instantiate SelectServerActionMenu");
        }

        Task<Result> ICommand.ExecuteAsync()
        {
            return ExecuteAsync();
        }

        public new async Task<Result> ExecuteAsync()
        {
            _log.Info("Begin: SelectServerActionMenu.ExecuteAsync");

            if (!_state.ClientSelected)
            {
                ReturnToParent = false;
                _log.Error($"Error: User tried to perform a server action before selecting a remote server/" +
                           $" (SelectServerActionMenu.ExecuteAsync)");
                return Result.Fail(SharedFunctions.NoClientSelectedError);
            }

            var exit = false;
            Result result = null;

            while (!exit)
            {
                var userSelection = 0;
                while (userSelection == 0)
                {
                    MenuFunctions.DisplayMenu(MenuText, Options);
                    var input = Console.ReadLine();

                    var validationResult = MenuFunctions.ValidateUserInput(input, OptionCount);
                    if (validationResult.Failure)
                    {
                        Console.WriteLine(validationResult.Error);
                        continue;
                    }

                    userSelection = validationResult.Value;
                }

                var selectedOption = Options[userSelection - 1];
                result = await selectedOption.ExecuteAsync();
                exit = selectedOption.ReturnToParent;

                if (result.Success) continue;

                _log.Error($"Error: {result.Error} (SelectServerActionMenu.ExecuteAsync)");
                Console.WriteLine(result.Error);
                exit = true;
            }

            _log.Info("Complete: SelectServerActionMenu.ExecuteAsync");
            return result;
        }
    }
}
