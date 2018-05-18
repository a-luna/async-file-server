namespace ServerConsole.Commands.Menus
{
    using System;
    using System.IO;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Extensions;
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;
    using AaronLuna.ConsoleProgressBar;

    using ServerCommands;

    using TplSockets;

    class MainMenu : MenuLoop
    {
        readonly AppState _state;
        readonly Logger _log = new Logger(typeof(MainMenu));

        public MainMenu(AppState state)
        {
            ReturnToParent = true;
            ItemText = "Main menu";
            MenuText = "\nMenu for TPL socket server:";
            MenuOptions = new List<ICommand>();

            var selectServerCommand = new SelectRemoteServerMenu(state);
            var selectActionCommand = new SelectServerActionMenu(state);
            var changeSettingsCommand = new ChangeSettingsMenu(state);
            var shutdownCommand = new ShutdownServerCommand();

            MenuOptions.Add(selectServerCommand);
            MenuOptions.Add(selectActionCommand);
            MenuOptions.Add(changeSettingsCommand);
            MenuOptions.Add(shutdownCommand);

            _state = state;
            
        }

        public new async Task<Result> ExecuteAsync()
        {
            var exit = false;
            Result result = null;

            while (!exit)
            {
                Console.WriteLine($"Server is listening for incoming requests on port {_state.LocalServer.Info.Port}\nLocal IP: {_state.LocalServer.Info.LocalIpAddress}\nPublic IP: {_state.LocalServer.Info.PublicIpAddress}\n");
                var selectedOption = Menu.GetUserSelection(MenuText, MenuOptions);
                exit = selectedOption.ReturnToParent;
                result = await selectedOption.ExecuteAsync().ConfigureAwait(false);

                if (result.Success) continue;
                Console.WriteLine($"{Environment.NewLine}Error: {result.Error}");

                if (result.Error.Contains(Resources.Error_NoClientSelectedError))
                {
                    Console.WriteLine("Press Enter to return to main menu.");
                    Console.ReadLine();
                    continue;
                }

                _log.Error($"Error: {result.Error} (MainMenu.ExecuteAsync)");
                exit = SharedFunctions.PromptUserYesOrNo("Exit program?");
            }
            
            return result;
        }

    }
}
 