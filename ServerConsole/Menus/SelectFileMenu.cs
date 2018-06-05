namespace ServerConsole.Menus
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using SelectFileMenuItems;

    using TplSockets;

    class SelectFileMenu : IMenu
    {
        readonly AppState _state;
        readonly bool _sendFile;

        public SelectFileMenu(AppState state, bool sendFile)
        {
            _state = state;
            _sendFile = sendFile;

            ReturnToParent = false;
            
            ItemText = sendFile
                ? "Send file to remote server"
                : "Download file from remote server" + Environment.NewLine;

            MenuText = sendFile
                ? "Choose a file to send:"
                : "Choose a file to get:";

            MenuItems = new List<IMenuItem>();
        }
        
        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }

        public async Task<Result> DisplayMenuAsync()
        {
            _state.DisplayCurrentStatus();

            var populateMenuResult = await PopulateMenuAsync();
            if (populateMenuResult.Failure)
            {
                return Result.Fail(populateMenuResult.Error);
            }

            Menu.DisplayMenu(MenuText, MenuItems);
            return Result.Ok();
        }

        public async Task<Result> ExecuteAsync()
        {
            _state.DoNotRefreshMainMenu = true;
            _state.DisplayCurrentStatus();

            var populateMenuResult = await PopulateMenuAsync();
            if (populateMenuResult.Failure)
            {
                return Result.Fail(populateMenuResult.Error);
            }

            var menuItem = await SharedFunctions.GetUserSelectionAsync(MenuText, MenuItems, _state);
            return await menuItem.ExecuteAsync();
        }

        async Task<Result> PopulateMenuAsync()
        {
            return _sendFile
                ? PopulateMenuForOutboundFileTransfer()
                : await PopulateMenuForInboundFileTransfer();
        }

        Result PopulateMenuForOutboundFileTransfer()
        {
            var getFileListResult = GetListOfFilesInLocalTransferFolder();
            if (getFileListResult.Failure)
            {
                return Result.Fail(getFileListResult.Error);
            }

            foreach (var file in getFileListResult.Value)
            {
                MenuItems.Add(new SendFileMenuItem(_state, file));
            }

            MenuItems.Add(new ReturnToParentMenuItem("Return to previous menu"));

            return Result.Ok();
        }

        Result<List<string>>  GetListOfFilesInLocalTransferFolder()
        {
            var emptyFolderError =
                "Transfer folder is empty, please place files in the path below:" +
                $"{Environment.NewLine}{_state.LocalServer.MyTransferFolderPath}";

            List<string> listOfFiles;
            try
            {
                listOfFiles = Directory.GetFiles(_state.LocalServer.MyTransferFolderPath).ToList();
            }
            catch (IOException ex)
            {
                return Result.Fail<List<string>>($"{ex.Message} ({ex.GetType()})");
            }

            return listOfFiles.Count > 0
                ? Result.Ok(listOfFiles)
                : Result.Fail<List<string>>(emptyFolderError);
        }

        async Task<Result> PopulateMenuForInboundFileTransfer()
        {
            var getFileListResult = await GetListOfFilesFromRemoteServer();
            if (getFileListResult.Failure)
            {
                return Result.Fail(getFileListResult.Error);
            }

            foreach (var fileInfoTuple in getFileListResult.Value)
            {
                MenuItems.Add(new GetFileMenuItem(_state, fileInfoTuple.Item1, fileInfoTuple.Item2));
            }

            MenuItems.Add(new ReturnToParentMenuItem("Return to previous menu"));

            return Result.Ok();
        }

        async Task<Result<FileInfoList>> GetListOfFilesFromRemoteServer()
        {
            var ipAddress = _state.SelectedServer.SessionIpAddress;
            var port = _state.SelectedServer.PortNumber;
            var remoteFolder = _state.SelectedServer.TransferFolder;

            _state.WaitingForFileListResponse = true;
            _state.NoFilesAvailableForDownload = false;
            _state.RequestedFolderDoesNotExist = false;

            var requestFileListResult =
                await _state.LocalServer.RequestFileListAsync(
                        ipAddress,
                        port,
                        remoteFolder)
                    .ConfigureAwait(false);

            if (requestFileListResult.Failure)
            {
                return Result.Fail<FileInfoList>(requestFileListResult.Error);
            }

            while (_state.WaitingForFileListResponse) { }

            if (_state.NoFilesAvailableForDownload)
            {
                return Result.Fail<FileInfoList>("There are no files in the requested folder.");
            }

            if (_state.RequestedFolderDoesNotExist)
            {
                return Result.Fail<FileInfoList>("The requested folder does not exist on the remote server.");
            }

            return Result.Ok(_state.LocalServer.RemoteServerFileList);
        }
    }
}
