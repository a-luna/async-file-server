namespace AaronLuna.AsyncFileServer.Console
{
    using System;
    using System.Threading.Tasks;

    using Common.Logging;

    static class Program
    {
        static async Task Main()
        {

            Logger.LogToConsole = false;
            Logger.Start("server.log");

            var logger = new Logger(typeof(Program));
            logger.Info("Application started");

            var exit = false;
            while (!exit)
            {
                System.Console.WriteLine($"{Environment.NewLine}Starting asynchronous server...{Environment.NewLine}");

                var server = new ServerApplication();
                var result = await server.RunAsync().ConfigureAwait(false);

                if (result.Failure)
                {
                    System.Console.WriteLine($"{Environment.NewLine}{result.Error}");

                    if (result.Error.Contains("Restarting"))
                    {
                        await Task.Delay(AaronLuna.Common.Constants.TwoSecondsInMilliseconds);
                        continue;
                    }
                }

                exit = true;
            }

            logger.Info("Application shutdown");
            Logger.ShutDown();
        }
    }
}
