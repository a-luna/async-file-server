namespace AaronLuna.AsyncFileServer.Console
{
    static class Resources
    {
        internal static readonly string MenuTierLabel_PendingRequests = "Pending Requests:";
        internal static readonly string MenuTierLabel_ViewLogs = "View Logs:";
        internal static readonly string MenuTierLabel_LocalServerOptions = "Local Server Options:";

        internal static readonly string Menu_ChangeSettings =
            "Items marked with an asterisk (*) will cause the server to restart after " +
            "any change is made, current value shown in parantheses for each item.";

        internal static readonly string Prompt_SetLocalPortNumber =
            "Enter the port number where this server will listen for incoming connections";

        internal static readonly string Prompt_GetCidrIp =
            "Enter the CIDR IP that describes the configuration of this local network " +
            "(i.e., enter 'a.b.c.d' portion of CIDR IP a.b.c.d/n)";

        internal static readonly string Prompt_GetCidrIpNetworkBitCount =
            "Enter the number of bits used to identify the network portion " +
            "of an IP address on your local network (i.e., enter the value " +
            "of 'n' in CIDR notation a.b.c.d/n)";

        internal static readonly string Prompt_SetRemoteServerIp =
            "Enter the IP address of the remote server in IPv4 format (i.e., a.b.c.d):";

        internal static readonly string Prompt_SetRemoteServerName =
            "Please enter a name to help identify this server (this name will not be shared " +
            "with the remote server, it is only stored locally):";

        internal static readonly string Prompt_SetRemoteServerPortNumber =
            "Enter the port number of the remote server:";

        internal static readonly string Prompt_ChangeRemoteServerIp =
            "Enter a new value for the IP address of the selected server in IPv4 format (i.e., a.b.c.d):";

        internal static readonly string Prompt_ChangeRemoteServerName =
            "Enter a new name for the remote server:";

        internal static readonly string Prompt_ChangeRemoteServerPortNumber =
            "Enter a new value for the port number of the selected server:";

        internal static readonly string Warning_UseLoopbackIp =
            "Unable to determine the local IP address for this machine, please " +
            "ensure that the CIDR IP address is correct for your LAN.\nWould you " +
            "like to use 127.0.0.1 (loopback) as the IP address of this server? " +
            "(you will only be able to communicate with other servers running on " +
            "the same local machine, this is only useful for testing)";

        internal static readonly string Error_NoClientSelectedError =
            "Please select a remote server before choosing an action to perform.";

        internal static readonly string Error_FileAlreadyExists =
            "\nThe client rejected the file transfer since a file by the same name already exists";

        internal static readonly string Error_FileTransferCancelled =
            "\nCancelled file transfer, client stopped receiving data and file transfer is incomplete.";

        internal static readonly string Error_FileTransferStalledErrorMessage =
            "Aborting file transfer, client says that data is no longer being received";
    }
}
