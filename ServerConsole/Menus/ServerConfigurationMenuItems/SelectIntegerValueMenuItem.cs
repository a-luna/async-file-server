namespace ServerConsole.Menus.ServerConfigurationMenuItems
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class SelectIntegerValueMenuItem : IMenuItem
    {
        public SelectIntegerValueMenuItem(string itemText)
        {
            ReturnToParent = false;
            ItemText = itemText;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>) Execute);
        }

        Result Execute()
        {
            return Result.Ok();
        }
    }
}
