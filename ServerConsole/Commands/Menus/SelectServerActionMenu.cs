namespace ServerConsole.Commands.Menus
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using ServerCommands;

    class SelectServerActionMenu : SelectionMenuLoop, ICommand
    {
        AppState _state;

        public SelectServerActionMenu(AppState state)
        {
            ReturnToParent = false;
            ItemText = "All server actions";
            MenuText = $"\nServer is listening for incoming requests on port {state.MyServerPort}" +
                       $"\nLocal IP:\t{state.MyLocalIpAddress}" +
                       $"\nPublic IP:\t{state.MyPublicIpAddress}" +
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
                return Result.Fail(ConsoleStatic.NoClientSelectedError);
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
                Console.WriteLine(result.Error);
                exit = true;
            }

            return result;
        }
    }
}
