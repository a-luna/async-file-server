namespace AaronLuna.AsyncFileServer.Model
{
    using System.IO;
    using System.Net;

    using Common.IO;

    public class FileTransfer
    {
        public FileTransfer()
        {            
            MyLocalIpAddress = IPAddress.None;
            MyPublicIpAddress = IPAddress.None;
            RemoteServerIpAddress = IPAddress.None;

            LocalFilePath = string.Empty;
            LocalFolderPath = string.Empty;
            RemoteFilePath = string.Empty;
            RemoteFolderPath = string.Empty;
            RemoteServerName = string.Empty;
        }
        
        public IPAddress MyLocalIpAddress { get; set; }
        public IPAddress MyPublicIpAddress { get; set; }
        public int MyServerPortNumber { get; set; }
        public IPAddress RemoteServerIpAddress { get; set; }
        public int RemoteServerPortNumber { get; set; }
        public string RemoteServerName { get; set; }

        public string LocalFilePath { get; set; }
        public string LocalFolderPath { get; set; }
        public string RemoteFilePath { get; set; }
        public string RemoteFolderPath { get; set; }
        public string FileName => Path.GetFileName(LocalFilePath);

        public long FileSizeInBytes { get; set; }
        public string FileSizeString => FileHelper.FileSizeToString(FileSizeInBytes);

        public FileTransfer Duplicate()
        {
            var shallowCopy = (FileTransfer)MemberwiseClone();

            shallowCopy.MyLocalIpAddress = new IPAddress(MyLocalIpAddress.GetAddressBytes());
            shallowCopy.MyPublicIpAddress = new IPAddress(MyPublicIpAddress.GetAddressBytes());
            shallowCopy.RemoteServerIpAddress = new IPAddress(RemoteServerIpAddress.GetAddressBytes());

            shallowCopy.LocalFilePath = string.Copy(LocalFilePath);
            shallowCopy.LocalFolderPath = string.Copy(LocalFolderPath);
            shallowCopy.RemoteFilePath = string.Copy(RemoteFilePath);
            shallowCopy.RemoteFolderPath = string.Copy(RemoteFolderPath);

            return shallowCopy;
        }
    }
}
