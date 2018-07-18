namespace AaronLuna.AsyncFileServer.Model
{
    using System.Collections.Generic;
    using System.Linq;

    using Common.Linq;

    public static class ServerEventFilter
    {
        public static List<ServerEvent> ApplyFilter(this List<ServerEvent> log, LogLevel logLevel)
        {
            log.RemoveAll(DoNotDisplayInLog);

            if (logLevel == LogLevel.Info)
            {
                log.RemoveAll(LogLevelIsDebugOnly);
            }

            return
                log.DistinctBy(e => new { e.TimeStamp, e.EventType })
                    .OrderBy(e => e.TimeStamp)
                    .ToList();
        }

        static bool DoNotDisplayInLog(ServerEvent serverEvent)
        {
            return serverEvent.DoNotDisplayInLog;
        }

        static bool LogLevelIsDebugOnly(ServerEvent serverEvent)
        {
            return serverEvent.LogLevelIsDebugOnly;
        }
    }
}
