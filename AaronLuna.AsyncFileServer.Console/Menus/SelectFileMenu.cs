namespace AaronLuna.AsyncFileServer.Console.Menus
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    using SelectFileMenuItems;
    using Model;
    using Common.Console.Menu;
    using Common.Result;

    class SelectFileMenu : IMenu
    {
        readonly AppState _state;
        readonly bool _sendFile;
        bool _fileListResponseTimeout;

        public SelectFileMenu(AppState state, bool sendFile)
        {
            _state = state;
            _sendFile = sendFile;

            ReturnToParent = false;
            
            ItemText = sendFile
                ? "Send file"
                : "Download file";

            MenuText = sendFile
                ? "Choose a file to send:"
                : "Choose a file to get:";

            MenuItems = new List<IMenuItem>();
        }
        
        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }
        
        public async Task<Result> ExecuteAsync()
        {
            _state.DoNotRefreshMainMenu = true;
            SharedFunctions.DisplayLocalServerInfo(_state);

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
            var ipAddress = _state.SelectedServerInfo.SessionIpAddress;
            var port = _state.SelectedServerInfo.PortNumber;
            var remoteFolder = _state.SelectedServerInfo.TransferFolder;

            _state.WaitingForFileListResponse = true;
            _state.NoFilesAvailableForDownload = false;
            _state.RequestedFolderDoesNotExist = false;
            _fileListResponseTimeout = false;

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

            var timeoutTask = Task.Run(FileListResponseTimeoutTask);
            while (_state.WaitingForFileListResponse)
            {
                if (_fileListResponseTimeout) break;
            }

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

        async Task FileListResponseTimeoutTask()
        {
            await Task.Delay(AaronLuna.Common.Constants.OneSecondInMilliseconds);
            _fileListResponseTimeout = true;
        }
    }
}
