namespace ServerConsole.Model
{
    using AaronLuna.Common.Console.Menu;

    using TplSocketServer;

    public interface IAppSettingsCommand : ICommand<AppSettings> { }
    public interface ITplSocketServerCommand : ICommand<TplSocketServer> { }
}
