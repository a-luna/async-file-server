namespace ServerConsole.Commands.Menus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    using CompositeCommands;
    using Getters;

    class SelectRemoteServerMenu : MenuSingleChoice, ICommand
    {
        readonly Logger _log = new Logger(typeof(SelectRemoteServerMenu));
        readonly AppState _state;

        public SelectRemoteServerMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Select remote server";
            MenuText = "\nChoose a remote server:";
            MenuOptions = new List<ICommand>();
        }

        Task<Result> ICommand.ExecuteAsync()
        {
            return ExecuteAsync();
        }

        public new async Task<Result> ExecuteAsync()
        {
            var localConnectionInfo = _state.ReportLocalServerConnectionInfo();
            var remoteConnectionInfo = _state.ReportRemoteServerConnectionInfo();

            Console.Clear();
            Console.WriteLine(localConnectionInfo);
            Console.WriteLine(remoteConnectionInfo);

            PopulateMenu();
            var selectedOption = Menu.GetUserSelection(MenuText, MenuOptions);
            return await selectedOption.ExecuteAsync().ConfigureAwait(false);
        }

        void PopulateMenu()
        {
            MenuOptions.Clear();

            foreach (var server in _state.Settings.RemoteServers)
            {
                MenuOptions.Add(new GetSelectedRemoteServerCommand(_state, server));
            }

            MenuOptions.Add(new GetRemoteServerInfoFromUserCommand(_state));
            MenuOptions.Add(new ReturnToParentCommand("Return to main menu", false));
        }
    }
}
