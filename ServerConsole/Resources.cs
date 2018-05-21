namespace ServerConsole
{
    static class Resources
    {
        internal static readonly string Warning_UseLoopbackIp =
            "Unable to determine the local IP address for this machine, please " +
            "ensure that the CIDR IP address is correct for your LAN.\nWould you " +
            "like to use 127.0.0.1 (loopback) as the IP address of this server? " +
            "(you will only be able to communicate with other servers running on " +
            "the same local machine, this is only useful for testing)";

        internal static readonly string Error_NoClientSelectedError = "Please select a remote server before choosing an action to perform.";

        internal static readonly string Error_FileAlreadyExists =
            "\nThe client rejected the file transfer since a file by the same name already exists";

        internal static readonly string Error_FileTransferCancelled =
            "\nCancelled file transfer, client stopped receiving data and file transfer is incomplete.";

        internal static readonly string Error_FileTransferStalledErrorMessage =
            "Aborting file transfer, client says that data is no longer being received";
    }
}
