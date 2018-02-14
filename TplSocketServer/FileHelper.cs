namespace TplSocketServer
{
    using System;
    using System.IO;
    using System.Threading.Tasks;

    public class FileHelper
    {
        public bool ErrorOccurred { get; set; }
        public string ErrorMessage { get; set; }

        public FileHelper()
        {
            ErrorOccurred = false;
            ErrorMessage = string.Empty;
        }

        public void DeleteFileIfAlreadyExists(string filePath)
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists) return;

            try
            {
                fi.Delete();
            }
            catch (IOException ex)
            {
                HandleException(ex);
            }
        }

        public async Task WriteBytesToFileAsync(string filePath, byte[] buffer, int length)
        {
            await Task.Factory.StartNew(() =>
            {
                try
                {
                    using (var fs = new FileStream(filePath, FileMode.Append))
                    using (var bw = new BinaryWriter(fs))
                    {
                        bw.Write(buffer, 0, length);
                        bw.Close();
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    HandleException(ex);
                }
            });
        }

        private void HandleException(Exception ex)
        {
            ErrorOccurred = true;
            ErrorMessage = ex.Message;
        }
    }
}
