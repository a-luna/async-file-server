using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AaronLuna.AsyncSocketServer.FileTransfers
{
    public class FileInfoList : List<(string fileName, string folderPath, long fileSizeBytes)>
    {
        public const string FileInfoSeparator = "\u001f";
        public const string FileSeparator = "\u001e";

        public FileInfoList() { }

        public FileInfoList(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;

            var fileList =
                Directory.GetFiles(folderPath).ToList()
                    .Select(f => new FileInfo(f)).Where(fi => !fi.Name.StartsWith('.'))
                    .Select(fi => fi.ToString()).ToList();

            foreach (var file in fileList)
            {
                var fileInfo =
                    (fileName: Path.GetFileName(file),
                        folderPath: Path.GetDirectoryName(file),
                        fileSizeBytes: new FileInfo(file).Length);

                Add(fileInfo);
            }
        }

        public static FileInfoList Parse(string delimtedFileInfoString)
        {
            var fileInfoList = new FileInfoList();
            foreach (var infoString in delimtedFileInfoString.Split(FileSeparator))
            {
                var infoSplit = infoString.Split(FileInfoSeparator);
                if (infoSplit.Length != 3) continue;

                var fileName = infoSplit[0];
                var folderPath = infoSplit[1];
                if (!long.TryParse(infoSplit[2], out var fileSizeBytes)) continue;

                var fi = (fileName: fileName, folderPath: folderPath, fileSizeBytes: fileSizeBytes);
                fileInfoList.Add(fi);
            }

            return fileInfoList;
        }
    }
}
