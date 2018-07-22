using AaronLuna.AsyncFileServer.Controller;

namespace AaronLuna.AsyncFileServer.Console.Menus.PendingRequestsMenus.PendingRequestsMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    class ProcessInboundFileTransferMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly FileTransferController _fileTransfer;

        public ProcessInboundFileTransferMenuItem(AppState state, FileTransferController fileTransfer)
        {
            _state = state;
            _fileTransfer = fileTransfer;

            ReturnToParent = false;
            ItemText = fileTransfer.ToString();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            SharedFunctions.DisplayLocalServerInfo(_state);

            var transferRequest = _fileTransfer.InboundRequestDetails();

            var downloadPrompt = transferRequest + Environment.NewLine +
                                 "Would you like to begin downloading this file?";
            
            var beginTransfer =
                SharedFunctions.PromptUserYesOrNo(_state, downloadPrompt);

            if (!beginTransfer)
            {
                var rejectPrompt =
                    $"Would you like to reject the file transfer?{Environment.NewLine}{Environment.NewLine}" +
                    $"If you select No, the file transfer will remain in Pending state allowing you to download the file at a later time.{Environment.NewLine}{Environment.NewLine}" +
                    $"If you select Yes, the file transfer will be rejected and removed from this list.{Environment.NewLine}";

                var rejectTransfer = SharedFunctions.PromptUserYesOrNo(_state, rejectPrompt);
                if (!rejectTransfer) return Result.Ok();

                var rejectResult = await _state.LocalServer.RejectInboundFileTransferAsync(_fileTransfer);
                if (rejectResult.Failure)
                {
                    return rejectResult;
                }
                
                return Result.Fail("File transfer was rejected.");
            }

            SharedFunctions.DisplayLocalServerInfo(_state);
            var transferResult = await _state.LocalServer.AcceptInboundFileTransferAsync(_fileTransfer);

            Console.WriteLine($"{Environment.NewLine}Press enter to return to the main menu.");
            Console.ReadLine();

            _state.SignalReturnToMainMenu.Set();
            return transferResult;
        }
    }
}
