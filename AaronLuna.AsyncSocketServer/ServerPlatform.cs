using System;

namespace AaronLuna.AsyncSocketServer
{
    public enum ServerPlatform
    {
        None,
        Unix,
        Windows
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
                case PlatformID.Xbox:
                    return ServerPlatform.Windows;

                case PlatformID.Unix:
                case PlatformID.MacOSX:
                    return ServerPlatform.Unix;

                default:
                    return ServerPlatform.None;
            }
        }
    }
}
