namespace ServerConsole.Commands.Menus
{
    using System.Collections.Generic;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;

    using CompositeCommands;

    using Getters;

    class SelectRemoteServerMenu : MenuSingleChoice
    {
        readonly Logger _log = new Logger(typeof(SelectRemoteServerMenu));

        public SelectRemoteServerMenu(AppState state)
        {
            ReturnToParent = false;
            ItemText = "Select remote server";
            MenuText = "\nChoose a remote server for this request:";
            MenuOptions = new List<ICommand>();

            var savedClients = new List<ICommand>();
            foreach (var server in state.Settings.RemoteServers)
            {
                savedClients.Add(new GetSelectedRemoteServerCommand(state, server));
            }

            if (savedClients.Count > 0)
            {
                MenuOptions.AddRange(savedClients);
            }

            var addNewClientCommand = new GetRemoteServerInfoFromUserCommand(state);
            var returnToParentCommand = new ReturnToParentCommand("Return to main menu", false);
            
            MenuOptions.Add(addNewClientCommand);
            MenuOptions.Add(returnToParentCommand);
        }
    }
}
