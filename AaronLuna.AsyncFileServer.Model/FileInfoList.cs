namespace AaronLuna.AsyncFileServer.Model
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    public class FileInfoList : List<(string fileName, string folderPath, long fileSizeBytes)>
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
                var fileInfo =
                    (fileName: Path.GetFileName(file),
                        folderPath: Path.GetDirectoryName(file),
                        fileSizeBytes: new FileInfo(file).Length);

                Add(fileInfo);
            }
        }
    }
}
