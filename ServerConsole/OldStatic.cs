namespace ServerConsole
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;

    using AaronLuna.Common.IO;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    using TplSocketServer;

    public static class OldStatic
    {

        public static Result<string> ChooseFileToSend(string transferFolderPath)
        {
            List<string> listOfFiles;
            try
            {
                listOfFiles = Directory.GetFiles(transferFolderPath).ToList();
            }
            catch (IOException ex)
            {
                return Result.Fail<string>($"{ex.Message} ({ex.GetType()})");
            }

            if (listOfFiles.Count == 0)
            {
                return Result.Fail<string>(
                    $"Transfer folder is empty, please place files in the path below:\n{transferFolderPath}\n\nReturning to main menu...");
            }

            var fileMenuChoice = 0;
            var totalMenuChoices = listOfFiles.Count + 1;
            var returnToPreviousMenu = totalMenuChoices;

            while (fileMenuChoice == 0)
            {
                Console.WriteLine("Choose a file to send:");

                foreach (var i in Enumerable.Range(0, listOfFiles.Count))
                {
                    var fileName = Path.GetFileName(listOfFiles[i]);
                    var fileSize = new FileInfo(listOfFiles[i]).Length;
                    Console.WriteLine($"{i + 1}. {fileName} ({FileHelper.FileSizeToString(fileSize)})");
                }

                Console.WriteLine($"{returnToPreviousMenu}. Return to Previous Menu");

                var input = Console.ReadLine();

                var validationResult = ConsoleStatic.ValidateNumberIsWithinRange(input, 1, totalMenuChoices);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                fileMenuChoice = validationResult.Value;
            }

            if (fileMenuChoice == returnToPreviousMenu)
            {
                return Result.Fail<string>("Returning to main menu");
            }

            return Result.Ok(listOfFiles[fileMenuChoice - 1]);
        }

        public static bool PromptUserYesOrNo(string prompt)
        {
            var shutdownChoice = 0;
            while (shutdownChoice is 0)
            {
                Console.WriteLine($"\n{prompt}");
                Console.WriteLine("1. Yes");
                Console.WriteLine("2. No");

                var input = Console.ReadLine();
                var validationResult = ConsoleStatic.ValidateNumberIsWithinRange(input, 1, 2);
                if (validationResult.Failure)
                {
                    Console.WriteLine(validationResult.Error);
                    continue;
                }

                shutdownChoice = validationResult.Value;
            }

            return shutdownChoice == 1;
        }       

    }
}
