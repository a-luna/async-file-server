using System;
using System.Collections.Generic;
using System.Text;
using AaronLuna.AsyncFileServer.Controller;
using AaronLuna.AsyncFileServer.Model;

namespace AaronLuna.AsyncFileServer.Test.Model
{
    class FileTransferStalledController : FileTransferController
    {
        public FileTransferStalledController(int id, ServerSettings settings) : base(id, settings)
        {
        }
    }
}
