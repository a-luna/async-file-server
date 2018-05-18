namespace ServerConsole
{
    static class Resources
    {

        internal static readonly string Error_NoClientSelectedError = "Please select a remote server before choosing an action to perform.";

        internal static readonly string Error_FileAlreadyExists = 
            "\nThe client rejected the file transfer since a file by the same name already exists";

        internal static readonly string Error_FileTransferCancelled =
            "\nCancelled file transfer, client stopped receiving data and file transfer is incomplete.";

        internal static readonly string Error_FileTransferStalledErrorMessage =
            "Aborting file transfer, client says that data is no longer being received";
    }
}
