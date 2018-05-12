namespace ServerConsole.Commands.Setters
{
    using System.Threading.Tasks;
    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;
    using TplSockets;

    class SetMyCidrIpCommand : ICommand
    {
        readonly AppState _state;

        public SetMyCidrIpCommand(AppState state)
        {
            ReturnToParent = false;
            ItemText = "Change CIDR IP that describes the LAN config for this server";

            _state = state;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            const string prompt = "Enter the CIDR IP that describes the configuration of this local network (i.e., enter 'a.b.c.d' portion of CIDR IP a.b.c.d/n)";
            var cidrIp = SharedFunctions.GetIpAddressFromUser(prompt);
            var cidrNetworkBitCount = SharedFunctions.GetCidrIpNetworkBitCountFromUser();
            _state.Settings.LocalNetworkCidrIp = $"{cidrIp}/{cidrNetworkBitCount}";

            AppSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);
            await Task.Delay(1);
            return Result.Ok();
        }
    }
}
