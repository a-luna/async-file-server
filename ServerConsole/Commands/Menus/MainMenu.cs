using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AaronLuna.Common.Result;
using ServerConsole.Commands.ServerCommands;

namespace ServerConsole.Commands.Menus
{
    using AaronLuna.Common.Console.Menu;

    class MainMenu : SelectionMenuLoop
    {
        public MainMenu(AppState state)
        {

            ReturnToParent = true;
            ItemText = "Main menu";
            MenuText = "\nMenu for TPL socket server:";
            Options = new List<ICommand>();

            var selectServerCommand = new SelectRemoteServerMenu(state);
            var selectActionCommand = new SelectServerActionMenu(state);
            var changeSettingsCommand = new ChangeSettingsMenu(state);
            var shutdownCommand = new ShutdownServerCommand();

            Options.Add(selectServerCommand);
            Options.Add(selectActionCommand);
            Options.Add(changeSettingsCommand);
            Options.Add(shutdownCommand);
        }

        public new async Task<Result> ExecuteAsync()
        {
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
                Console.WriteLine($"{Environment.NewLine}Error: {result.Error}");

                if (result.Error.Contains(ConsoleStatic.NoClientSelectedError))
                {
                    Console.WriteLine("Press Enter to return to main menu.");
                    Console.ReadLine();
                    continue;
                }

                exit = ConsoleStatic.PromptUserYesOrNo($"Exit program?");
            }

            return result;
        }
    }
}
