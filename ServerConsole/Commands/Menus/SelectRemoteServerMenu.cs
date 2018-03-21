using ServerConsole.Commands.CompositeCommands;

namespace ServerConsole.Commands.Menus
{
    using System.Linq;

    using AaronLuna.Common.Console.Menu;

    using TplSocketServer;

    using Getters;

    class SelectRemoteServerMenu : SelectionMenuSingleChoice<RemoteServer>
    {
        public SelectRemoteServerMenu(AppState state)
        {
            ReturnToParent = false;
            ItemText = "Select remote server";
            MenuText = "\nChoose a remote server for this request:";

            Options = 
                state.Settings.RemoteServers.Select(
                        s => (ICommand<RemoteServer>) new GetSelectedRemoteServerCommand(s)).ToList();

            var addNewClientCommand = new GetRemoteServerInfoFromUserCommand(state);
            var returnToParentCommand = new ReturnToParentCommand<RemoteServer>("Return to main menu");
            
            Options.Add(addNewClientCommand);
            Options.Add(returnToParentCommand);
        }
    }
}
