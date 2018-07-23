namespace AaronLuna.AsyncFileServer.Model
{
    using System.IO;
    using System.Net;

    using Common.IO;

    public class FileTransfer
    {
        public FileTransfer()
        {   
            FileName = string.Empty;
            LocalFolderPath = string.Empty;
            RemoteFolderPath = string.Empty;
            RemoteServerIpAddress = IPAddress.None;
        }
        
        public string FileName { get; set; }
        public long FileSizeInBytes { get; set; }
        public string LocalFolderPath { get; set; }
        public string RemoteFolderPath { get; set; }
        public IPAddress RemoteServerIpAddress { get; set; }
        public int RemoteServerPortNumber { get; set; }

        public string LocalFilePath => Path.Combine(LocalFolderPath, FileName);
        public string RemoteFilePath => Path.Combine(RemoteFolderPath, FileName);
        public string FileSizeString => FileHelper.FileSizeToString(FileSizeInBytes);

        public FileTransfer Duplicate()
        {
            var shallowCopy = (FileTransfer)MemberwiseClone();
            
            shallowCopy.FileName = string.Copy(FileName);
            shallowCopy.LocalFolderPath = string.Copy(LocalFolderPath);
            shallowCopy.RemoteFolderPath = string.Copy(RemoteFolderPath);
            shallowCopy.RemoteServerIpAddress = new IPAddress(RemoteServerIpAddress.GetAddressBytes());

            return shallowCopy;
        }
    }
}
