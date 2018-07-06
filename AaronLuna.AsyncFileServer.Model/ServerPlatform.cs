namespace AaronLuna.AsyncFileServer.Model
{
    using System;

    public enum ServerPlatform
    {
        None,
        MacOSX,
        Unix,
        Windows,
        XBox
    }

    public static class ServePlatformExtensions
    {
        public static ServerPlatform ToServerPlatform(this PlatformID platform)
        {
            switch (platform)
            {
                case PlatformID.Win32S:
                case PlatformID.Win32Windows:
                case PlatformID.Win32NT:
                case PlatformID.WinCE:
                    return ServerPlatform.Windows;

                case PlatformID.Unix:
                    return ServerPlatform.Unix;

                case PlatformID.Xbox:
                    return ServerPlatform.XBox;

                case PlatformID.MacOSX:
                    return ServerPlatform.MacOSX;

                default:
                    return ServerPlatform.None;
            }
        }
    }
}
