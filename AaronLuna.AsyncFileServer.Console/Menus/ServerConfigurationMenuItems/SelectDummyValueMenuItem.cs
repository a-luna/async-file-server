namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    class SelectDummyValueMenuItem : IMenuItem
    {
        public SelectDummyValueMenuItem(string itemText)
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
