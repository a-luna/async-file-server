namespace ServerConsole.Commands.Menus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    using ServerCommands;

    class MainMenu : MenuLoop
    {
        readonly AppState _state;
        readonly Logger _log = new Logger(typeof(MainMenu));

        public MainMenu(AppState state)
        {
            _state = state;

            ReturnToParent = true;
            ItemText = "Main menu";
            MenuText = "Main Menu:";
            MenuOptions = new List<ICommand>();

            var selectServerCommand = new SelectRemoteServerMenu(state);
            var selectActionCommand = new SelectServerActionMenu(state);
            var changeSettingsCommand = new ChangeSettingsMenu(state);
            var shutdownCommand = new ShutdownServerCommand();

            MenuOptions.Add(selectServerCommand);
            MenuOptions.Add(selectActionCommand);
            MenuOptions.Add(changeSettingsCommand);
            MenuOptions.Add(shutdownCommand);
        }

        public new async Task<Result> ExecuteAsync()
        {
            var exit = false;
            Result result = null;

            while (!exit)
            {
                var localConnectionInfo = _state.ReportLocalServerConnectionInfo();
                var remoteConnectionInfo = _state.ReportRemoteServerConnectionInfo();

                Console.Clear();
                Console.WriteLine(localConnectionInfo);
                Console.WriteLine(remoteConnectionInfo);

                var selectedOption = Menu.GetUserSelection(MenuText, MenuOptions);
                exit = selectedOption.ReturnToParent;
                result = await selectedOption.ExecuteAsync().ConfigureAwait(false);

                if (result.Success) continue;
                Console.WriteLine($"{Environment.NewLine}Error: {result.Error}");
                exit = SharedFunctions.PromptUserYesOrNo("Exit program?");
            }

            return result;
        }

        public void DisplayMenu()
        {
            var localConnectionInfo = _state.ReportLocalServerConnectionInfo();
            var remoteConnectionInfo = _state.ReportRemoteServerConnectionInfo();

            Console.Clear();
            Console.WriteLine(localConnectionInfo);
            Console.WriteLine(remoteConnectionInfo);

            Menu.DisplayMenu(MenuText, MenuOptions);
        }
    }
}
