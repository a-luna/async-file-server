namespace ServerConsole.Commands.Menus
{
    using System;
    using System.Threading.Tasks;
    using AaronLuna.Common.Console.Menu;

    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    using Getters;
    using Setters;

    class ChangeSettingsMenu : MenuLoop, ICommand
    {
        readonly Logger _log = new Logger(typeof(ChangeSettingsMenu));
        readonly AppState _state;

        public ChangeSettingsMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Change settings";
            MenuText = "\nWhat setting would you like to change?";

            MenuOptions.Add(new SetMyPortNumberCommand(state));
            MenuOptions.Add(new SetMyCidrIpCommand(state));
            MenuOptions.Add(new GetMyLocalIpAddressCommand(state));
            MenuOptions.Add(new GetMyPublicIpAddressCommand(state));
            MenuOptions.Add(new ReturnToParentCommand("Return to main menu", true));
        }

        Task<Result> ICommand.ExecuteAsync()
        {
            return ExecuteAsync();
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

                _log.Error($"Error: {result.Error} (SelectServerActionMenu.ExecuteAsync)");
                Console.WriteLine(result.Error);
                exit = true;
            }
            return result;
        }
    }
}
