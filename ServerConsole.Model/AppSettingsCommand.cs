namespace ServerConsole.Model
{
    using System.Threading.Tasks;
    using AaronLuna.Common.Result;

    using TplSocketServer;

    public abstract class AppSettingsCommand : IAppSettingsCommand
    {
        readonly string _itemText;
        readonly bool _returnToParent;

        public AppSettingsCommand(string itemText)
        {
            _itemText = itemText;
            _returnToParent = false;
        }

        public abstract Task<Result<AppSettings>> ExecuteAsync();
        public string GetItemText() { return _itemText; }
        public bool ReturnToParent() { return _returnToParent; }
    }
}
