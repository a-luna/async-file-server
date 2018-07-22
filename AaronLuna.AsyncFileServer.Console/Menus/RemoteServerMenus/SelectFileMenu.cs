namespace AaronLuna.AsyncFileServer.Console.Menus.RemoteServerMenus
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Extensions;
    using Common.Result;

    using Model;
    using SelectFileMenuItems;

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

            var populateMenuResult = await PopulateMenuAsync().ConfigureAwait(false);
            if (populateMenuResult.Failure)
            {
                return Result.Fail(populateMenuResult.Error);
            }

            var menuItem = SharedFunctions.GetUserSelection(MenuText, MenuItems, _state);
            return await menuItem.ExecuteAsync().ConfigureAwait(false);
        }

        async Task<Result> PopulateMenuAsync()
        {
            return _sendFile
                ? PopulateMenuForOutboundFileTransfer()
                : await PopulateMenuForInboundFileTransfer().ConfigureAwait(false);
        }

        Result PopulateMenuForOutboundFileTransfer()
        {
            var getFileListResult = GetListOfFilesInLocalTransferFolder();
            if (getFileListResult.Failure)
            {
                return Result.Fail(getFileListResult.Error);
            }

            var fileInfoList = getFileListResult.Value;
            foreach (var i in Enumerable.Range(0, fileInfoList.Count))
            {
                var isLastMenuItem = i.IsLastIteration(fileInfoList.Count);
                MenuItems.Add(new SendFileMenuItem(_state, fileInfoList[i], isLastMenuItem));
            }

            MenuItems.Add(new ReturnToParentMenuItem("Return to previous menu"));

            return Result.Ok();
        }

        Result<List<string>>  GetListOfFilesInLocalTransferFolder()
        {
            var emptyFolderError =
                "Transfer folder is empty, please place files in the path below:" +
                $"{Environment.NewLine}{_state.LocalServer.MyInfo.TransferFolder}";

            List<string> listOfFiles;
            try
            {
                listOfFiles =
                    Directory.GetFiles(_state.LocalServer.MyInfo.TransferFolder).ToList()
                        .Select(f => f)
                        .Where(f => !f.StartsWith('.'))
                        .ToList();
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
            var getFileListResult = await GetListOfFilesFromRemoteServer().ConfigureAwait(false);
            if (getFileListResult.Failure)
            {
                return Result.Fail(getFileListResult.Error);
            }

            var fileInfoList = getFileListResult.Value;
            foreach (var i in Enumerable.Range(0, fileInfoList.Count))
            {
                var fileName = fileInfoList[i].fileName;
                var folderPath = fileInfoList[i].folderPath;
                var fileSize = fileInfoList[i].fileSizeBytes;
                var isLastMenuItem = i.IsLastIteration(fileInfoList.Count);

                MenuItems.Add(new GetFileMenuItem(_state, fileName, folderPath, fileSize, isLastMenuItem));
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
            await Task.Delay(Common.Constants.OneSecondInMilliseconds).ConfigureAwait(false);
            _fileListResponseTimeout = true;
        }
    }
}
