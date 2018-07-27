namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus.LocalServerSettingsMenuItems
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    
    using Common.Console.Menu;
    using Common.IO;
    using Common.Result;

    using CommonMenuItems;

    class SetSocketBufferSizeMenu : IMenu
    {
        readonly AppState _state;
        readonly List<int> _bufferSizeValues;

        public SetSocketBufferSizeMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;

            ItemText = " Send/Receive Socket Buffer Size.........: " +
                       FileHelper.FileSizeToString(_state.Settings.SocketSettings.BufferSize);

            MenuText = "Select a value from the list below:";

            _bufferSizeValues = new List<int>
            {
                512,
                1024,
                1024 * 2,
                1024 * 4,
                1024 * 8,
                1024 * 16,
                1024 * 32,
                1024 * 64,
                1024 * 128,
                1024 * 256,
                1024 * 512,
                1024 * 1024,
            };

            MenuItems = new List<IMenuItem>
            {
                new SelectDummyValueMenuItem($" 0.5 KB       ({_bufferSizeValues[0]:N0} bytes)"),                   //  1
                new SelectDummyValueMenuItem($"   1 KB     ({_bufferSizeValues[1]:N0} bytes)"),                     //  2
                new SelectDummyValueMenuItem($"   2 KB     ({_bufferSizeValues[2]:N0} bytes)"),                     //  3
                new SelectDummyValueMenuItem($"   4 KB     ({_bufferSizeValues[3]:N0} bytes)"),                     //  4
                new SelectDummyValueMenuItem($"   8 KB     ({_bufferSizeValues[4]:N0} bytes)"),                     //  5
                new SelectDummyValueMenuItem($"  16 KB    ({_bufferSizeValues[5]:N0} bytes)"),                      //  6
                new SelectDummyValueMenuItem($"  32 KB    ({_bufferSizeValues[6]:N0} bytes)"),                      //  7
                new SelectDummyValueMenuItem($"  64 KB    ({_bufferSizeValues[7]:N0} bytes)"),                      //  8
                new SelectDummyValueMenuItem($" 128 KB   ({_bufferSizeValues[8]:N0} bytes)"),                       //  9
                new SelectDummyValueMenuItem($"256 KB   ({_bufferSizeValues[9]:N0} bytes)"),                        // 10
                new SelectDummyValueMenuItem($"512 KB   ({_bufferSizeValues[10]:N0} bytes)"),                       // 11
                new SelectDummyValueMenuItem($"  1 MB ({_bufferSizeValues[11]:N0} bytes){Environment.NewLine}"),    // 12
                new ReturnToParentMenuItem("Return to previous menu")
            };
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>)Execute);
        }

        Result Execute()
        {
            _state.DoNotRefreshMainMenu = true;
            SharedFunctions.DisplayLocalServerInfo(_state);

            var menuIndex = SharedFunctions.GetUserSelectionIndex(MenuText, MenuItems, _state);
            if (menuIndex > _bufferSizeValues.Count) return Result.Ok();

            _state.Settings.SocketSettings.BufferSize = _bufferSizeValues[menuIndex - 1];

            return Result.Ok();
        }
    }
}
