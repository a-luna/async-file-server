namespace ServerConsole.Menus.ServerConfigurationMenus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using ServerConfigurationMenuItems;

    class SetSocketBufferSizeMenu : IMenu
    {
        readonly AppState _state;
        readonly List<int> _bufferSizeValues;

        public SetSocketBufferSizeMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Change socket buffer size ({_state.Settings.SocketSettings.BufferSize})";
            MenuText = "Select a value from the list below:";

            _bufferSizeValues = new List<int>
            {
                1024,
                1024 * 2,
                1024 * 4,
                8192,
                8192 * 2,
                8192 * 4,
                8192 * 8,
                8192 * 16
            };

            MenuItems = new List<IMenuItem>
            {
                new SelectIntegerValueMenuItem($"1 Kb ({_bufferSizeValues[0]:N0} bytes)"),
                new SelectIntegerValueMenuItem($"2 Kb ({_bufferSizeValues[1]:N0} bytes)"),
                new SelectIntegerValueMenuItem($"4 Kb ({_bufferSizeValues[2]:N0} bytes)"),
                new SelectIntegerValueMenuItem($"1 KB ({_bufferSizeValues[3]:N0} bytes)"),
                new SelectIntegerValueMenuItem($"2 KB ({_bufferSizeValues[4]:N0} bytes)"),
                new SelectIntegerValueMenuItem($"4 KB ({_bufferSizeValues[5]:N0} bytes)"),
                new SelectIntegerValueMenuItem($"8 KB ({_bufferSizeValues[6]:N0} bytes)"),
                new SelectIntegerValueMenuItem($"16 KB ({_bufferSizeValues[7]:N0} bytes){Environment.NewLine}"),
                new ReturnToParentMenuItem("Return to previous menu")
            };
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.DoNotRefreshMainMenu = true;
            _state.DisplayCurrentStatus();

            var menuIndex = await SharedFunctions.GetUserSelectionIndexAsync(MenuText, MenuItems, _state);
            if (menuIndex > _bufferSizeValues.Count) return Result.Ok();

            _state.Settings.SocketSettings.BufferSize = _bufferSizeValues[menuIndex - 1];

            return Result.Ok();
        }
    }
}
