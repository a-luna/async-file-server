namespace ServerConsole
{
    static class Resources
    {
        internal static readonly string Menu_ChangeSettings =
            "Items marked with an asterisk (*) will cause the server to restart after " +
            "any change is made, current value shown in parantheses for each item.";

        internal static readonly string Prompt_SetLocalPortNumber =
            "Enter the port number where this server will listen for incoming connections";

        internal static readonly string Prompt_SetLanCidrIp =
            "Enter the CIDR IP that describes the configuration of this local network " +
            "(i.e., enter 'a.b.c.d' portion of CIDR IP a.b.c.d/n)";

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

    static class Constants
    {
        public static int OneHalfSecondInMilliseconds = 500;
        public static int OneSecondInMilliseconds = 1000;
        public static int TwoSecondsInMilliseconds = 2000;
        public static int ThreeSecondsInMilliseconds = 3000;
        public static int FourSecondsInMilliseconds = 4000;
        public static int FiveSecondsInMilliseconds = 5000;
        public static int TenSecondsInMilliseconds = 10000;
        public static int TwentySecondsInMilliseconds = 20000;
        public static int ThirtySecondsInMilliseconds = 30000;
    }
}
