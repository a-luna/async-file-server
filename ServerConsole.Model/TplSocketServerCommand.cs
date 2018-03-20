namespace ServerConsole.Model
{
    using System.Threading.Tasks;
    using AaronLuna.Common.Result;

    using TplSocketServer;

    public abstract class TplSocketServerCommand : ITplSocketServerCommand
    {
        readonly string _itemText;
        readonly bool _returnToParent;

        protected TplSocketServerCommand(string itemText)
        {
            _itemText = itemText;
            _returnToParent = false;
        }

        public abstract Task<Result<TplSocketServer>> ExecuteAsync();
        public string GetItemText() { return _itemText; }
        public bool ReturnToParent() { return _returnToParent; }
    }
}
