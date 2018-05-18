namespace ServerConsole
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Logging;

    static class Program
    {
        static async Task Main()
        {
            Logger.LogToConsole = false;
            Logger.Start("server.log");

            var logger = new Logger(typeof(Program));
            logger.Info("Application started");

            Console.WriteLine("\nStarting asynchronous server...\n");

            var server = new ServerApplication();
            var result = await server.RunAsync().ConfigureAwait(false);
            if (result.Failure)
            {
                Console.WriteLine(result.Error);
            }

            Console.WriteLine("Press enter to exit.");
            Console.ReadLine();

            logger.Info("Application shutdown");
            Logger.ShutDown();
        }
    }
}
