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
        readonly AppState _state;
        readonly Logger _log = new Logger(typeof(SelectServerActionMenu));

        public SelectServerActionMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "All server actions";
            MenuText = "Please make a choice from the menu below:";

            var sendTextMessageCommand = new SendTextMessageCommand(state);
            var sendFileCommand = new SendFileCommand();
            var getFileCommand = new GetFileCommand();
            var returnToMainMenuCommand = new ReturnToParentCommand("Return to main menu", true);

            MenuOptions.Add(sendTextMessageCommand);
            MenuOptions.Add(sendFileCommand);
            MenuOptions.Add(getFileCommand);
            MenuOptions.Add(returnToMainMenuCommand);
        }

        Task<Result> ICommand.ExecuteAsync()
        {
            return ExecuteAsync();
        }

        public new async Task<Result> ExecuteAsync()
        {
            if (!_state.ClientSelected)
            {
                var selectServer = new SelectRemoteServerMenu(_state);
                var selectServerResult = await selectServer.ExecuteAsync();
                if (selectServerResult.Failure)
                {
                    return selectServerResult;
                }
            }

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
                exit = true;
            }
            return result;
        }
    }
}
