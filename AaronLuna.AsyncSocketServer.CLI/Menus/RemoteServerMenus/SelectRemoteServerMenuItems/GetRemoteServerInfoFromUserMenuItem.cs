﻿using System;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.RemoteServerMenus.SelectRemoteServerMenuItems
{
    class GetRemoteServerInfoFromUserMenuItem : IMenuItem
    {
        readonly AppState _state;

        public GetRemoteServerInfoFromUserMenuItem(AppState state)
        {
            ReturnToParent = false;
            ItemText = $"Add remote server{Environment.NewLine}";

            _state = state;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.DoNotRequestServerInfo = true;
            _state.DoNotRefreshMainMenu = true;
            _state.PromptUserForServerName = true;
            _state.WaitingForUserToConfirmServerDetails = true;

            SharedFunctions.DisplayLocalServerInfo(_state);

            var remoteServerIp =
                SharedFunctions.GetIpAddressFromUser(_state, Resources.Prompt_SetRemoteServerIp);

            SharedFunctions.DisplayLocalServerInfo(_state);

            var remoteServerPort =
                SharedFunctions.GetPortNumberFromUser(
                    _state,
                    Resources.Prompt_SetRemoteServerPortNumber,
                    false);

            var serverInfo = new ServerInfo(remoteServerIp, remoteServerPort);

            var validateServerInfo = ValidateServerInfo(serverInfo);
            if (validateServerInfo.Failure)
            {
                return validateServerInfo;
            }

            var requestServerInfo = await
                SharedFunctions.RequestServerInfoAsync(
                    _state,
                    _state.SelectedServerInfo.SessionIpAddress,
                    _state.SelectedServerInfo.PortNumber);

            while (_state.WaitingForUserToConfirmServerDetails) { }

            _state.DoNotRequestServerInfo = false;
            _state.DoNotRefreshMainMenu = false;

            return requestServerInfo;
        }

         Result ValidateServerInfo(ServerInfo serverInfo)
        {
            var clientIp = serverInfo.SessionIpAddress;
            var clientPort = serverInfo.PortNumber;

            if (serverInfo.IsEqualTo(_state.LocalServer.MyInfo))
            {
                var error =
                    $"{clientIp}:{clientPort} is the same IP address " +
                    "and port number used by this server.";

                return Result.Fail(error);
            }

            var serverInfoAlreadyExists =
                SharedFunctions.ServerInfoAlreadyExists(serverInfo, _state.Settings.RemoteServers);

            if (serverInfoAlreadyExists)
            {
                _state.SelectedServerInfo =
                    SharedFunctions.GetRemoteServer(serverInfo, _state.Settings.RemoteServers).Value;

                return Result.Fail("Server is already added, returning to main menu.");
            }

            _state.SelectedServerInfo = serverInfo;

            return Result.Ok();
        }
    }
}
