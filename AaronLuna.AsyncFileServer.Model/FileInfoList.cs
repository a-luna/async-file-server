namespace AaronLuna.AsyncFileServer.Model
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    public class FileInfoList : List<(string filePath, long fileSizeBytes)>
    {
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
                var fileSize = new FileInfo(file).Length;
                (string filePath, long fileSizeBytes) fileInfo = (filePath: file, fileSizeBytes: fileSize);
                Add(fileInfo);
            }
        }
    }
}
