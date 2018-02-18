namespace TplSocketServer
{
    using System;
    using System.IO;

    public static class FileHelper
    {
        public static Result DeleteFileIfAlreadyExists(string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (!fi.Exists)
                {
                    return Result.Ok();
                }

                fi.Delete();
            }
            catch (IOException ex)
            {
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method FileHelper.DeleteFileIfAlreadyExists)");
            }

            return Result.Ok();
        }

        public static Result WriteBytesToFile(string filePath, byte[] buffer, int length)
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
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method FileHelper.DeleteFileIfAlreadyExists)");
            }

            return Result.Ok();
        }
    }
}
