using AaronLuna.Common.Console.Menu;

namespace ServerConsole.Commands.Menus
{
    class ChangeSettingsMenu : SelectionMenuLoop
    {
        public ChangeSettingsMenu(AppState state)
        {
            ReturnToParent = true;
            ItemText = "Change server settings";
            MenuText = "\nWhat setting would you like to change?";

        }
    }
}
