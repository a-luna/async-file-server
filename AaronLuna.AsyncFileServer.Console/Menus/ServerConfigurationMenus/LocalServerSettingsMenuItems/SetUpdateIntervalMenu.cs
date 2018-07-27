namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus.LocalServerSettingsMenuItems
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    
    using Common.Console.Menu;
    using Common.Result;

    using CommonMenuItems;

    class SetUpdateIntervalMenu : IMenu
    {
        readonly AppState _state;
        readonly List<float> _updateFrequencyValues;

        public SetUpdateIntervalMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = " Report File Transfer Progress Every.....: " +
                       $"{_state.Settings.TransferUpdateInterval:P2}";
            MenuText = "Select a value from the list below:";

            _updateFrequencyValues = new List<float>
            {
                0.001f,
                0.002f,
                0.0025f,
                0.005f,
                0.01f,
                0.02f
            };

            MenuItems = new List<IMenuItem>
            {
                new SelectDummyValueMenuItem($"{_updateFrequencyValues[0]:P2}"),
                new SelectDummyValueMenuItem($"{_updateFrequencyValues[1]:P2}"),
                new SelectDummyValueMenuItem($"{_updateFrequencyValues[2]:P2}"),
                new SelectDummyValueMenuItem($"{_updateFrequencyValues[3]:P2}"),
                new SelectDummyValueMenuItem($"{_updateFrequencyValues[4]:P2}"),
                new SelectDummyValueMenuItem($"{_updateFrequencyValues[5]:P2}{Environment.NewLine}"),
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
            if (menuIndex > _updateFrequencyValues.Count) return Result.Ok();

            _state.Settings.TransferUpdateInterval = _updateFrequencyValues[menuIndex - 1];
            _state.LocalServer.UpdateSettings(_state.Settings);

            return Result.Ok();
        }
    }
}
