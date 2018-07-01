namespace AaronLuna.AsyncFileServer.Console.Menus.ViewTextSessionsMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Model;
    using Common.Console.Menu;
    using Common.Result;

    class ViewTextSessionMenuItem : IMenuItem
    {
        readonly TextSession _textSession;

        public ViewTextSessionMenuItem(TextSession textSession)
        {
            _textSession = textSession;

            ReturnToParent = false;
            ItemText = textSession.ToString();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>) Execute);
        }

        Result Execute()
        {
            Console.WriteLine(Environment.NewLine);

            foreach (var textMessage in _textSession.Messages)
            {
                Console.WriteLine(textMessage + Environment.NewLine);
            }

            Console.WriteLine($"{Environment.NewLine}Press enter to return to the previous menu.");
            Console.ReadLine();

            return Result.Ok();
        }
    }
}
