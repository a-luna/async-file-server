﻿using System.Threading.Tasks;
using AaronLuna.Common.Result;

namespace ServerConsole.Commands.ServerCommands
{
    using AaronLuna.Common.Console.Menu;

    class ShutdownServerMenuItem : IMenuItem
    {
        AppState _state;

        public ShutdownServerMenuItem(AppState state)
        {
            _state = state;
            ReturnToParent = true;
            ItemText = "Shutdown";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return _state.LocalServer.ShutdownAsync();

        }
    }
}
