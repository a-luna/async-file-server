namespace AaronLuna.AsyncFileServer.Test
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;

    using Model;
    using Utilities;
    using Common.Network;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class BugVerificationUnitTests
    {
        IPAddress _localIp;
        string _cidrIp;
        string _testFilesFolder;

        [TestMethod]
        public void VerifyBug_ReceiveWindowsFileListOnUnix()
        {
            _localIp = IPAddress.Loopback;
            _cidrIp = "172.20.10.0/28";

            var getCidrIp = NetworkUtilities.GetCidrIp();
            if (getCidrIp.Success)
            {
                _cidrIp = getCidrIp.Value;
            }

            var getLocalIpResult = NetworkUtilities.GetLocalIPv4Address(_cidrIp);
            if (getLocalIpResult.Success)
            {
                _localIp = getLocalIpResult.Value;
            }

            const int remoteServerPort = 8022;

            var currentPath = Directory.GetCurrentDirectory();
            var index = currentPath.IndexOf("bin", StringComparison.Ordinal);
            _testFilesFolder = $"{currentPath.Remove(index - 1)}{Path.DirectorySeparatorChar}TestFiles{Path.DirectorySeparatorChar}";

            var windowsFilePaths = new List<string>
            {
                "C:\\Users\\aaronluna\\Desktop\\New folder\\Server\\AaronLuna.jpeg",
                "C:\\Users\\aaronluna\\Desktop\\New folder\\Server\\Git-2.16.2-64-bit.exe",
                "C:\\Users\\aaronluna\\Desktop\\New folder\\Server\\LINQPad5Setup.exe",
                "C:\\Users\\aaronluna\\Desktop\\New folder\\Server\\p4vinst64.exe",
                "C:\\Users\\aaronluna\\Desktop\\New folder\\Server\\systemrescuecd-x86-5.2.2.iso",
                "C:\\Users\\aaronluna\\Desktop\\New folder\\Server\\themeforest-6339019-total-responsive-multipurpose-wordpress-theme.zip",
                "C:\\Users\\aaronluna\\Desktop\\New folder\\Server\\Total.zip",
                "C:\\Users\\aaronluna\\Desktop\\New folder\\Server\\vlc-3.0.0-win32.exe"
            };

            var fileNames = new List<string>
            {
                "AaronLuna.jpeg",
                "Git-2.16.2-64-bit.exe",
                "LINQPad5Setup.exe",
                "p4vinst64.exe",
                "systemrescuecd-x86-5.2.2.iso",
                "themeforest-6339019-total-responsive-multipurpose-wordpress-theme.zip",
                "Total.zip",
                "vlc-3.0.0-win32.exe"
            };

            var fileSizes = new List<long>
            {
                432543,
                39139744,
                19304160,
                71650824,
                572006400,
                56463092,
                17277564,
                38911168
            };

            var windowsFileList = new FileInfoList
            {
                (filePath: windowsFilePaths[0], fileSizeBytes: fileSizes[0]),
                (filePath: windowsFilePaths[1], fileSizeBytes: fileSizes[1]),
                (filePath: windowsFilePaths[2], fileSizeBytes: fileSizes[2]),
                (filePath: windowsFilePaths[3], fileSizeBytes: fileSizes[3]),
                (filePath: windowsFilePaths[4], fileSizeBytes: fileSizes[4]),
                (filePath: windowsFilePaths[5], fileSizeBytes: fileSizes[5]),
                (filePath: windowsFilePaths[6], fileSizeBytes: fileSizes[6]),
                (filePath: windowsFilePaths[7], fileSizeBytes: fileSizes[7])
            };

            var fileListResponseBytes =
                ServerRequestDataBuilder.ConstructFileListResponse(
                    windowsFileList,
                    "*",
                    "|",
                    _localIp.ToString(),
                    remoteServerPort,
                    _testFilesFolder);

            var (_,
                _,
                _,
                _,
                _,
                _,
                _,
                _,
                _,
                _,
                _,
                fileInfoList,
                _,
                _,
                _,
                _,
                _) = ServerRequestDataReader.ReadRequestBytes(fileListResponseBytes);

            Assert.AreEqual(windowsFilePaths.Count, fileInfoList.Count);

            foreach (var i in Enumerable.Range(0, fileInfoList.Count))
            {
                Assert.AreEqual(fileNames[i], Path.GetFileName(fileInfoList[i].filePath));
            }
        }
    }
}
