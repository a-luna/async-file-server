namespace TplSocketServer
{
    using System;

    public static class ExtensionMethods
    {
        public static string ToFormattedString(this TimeSpan timeSpan)
        {
            if (timeSpan.TotalSeconds <= 0)
            {
                return string.Empty;
            }

            var s = string.Empty;

            var numYears = timeSpan.Days / 365;
            if (numYears > 0)
            {
                s += $"{numYears}y ";
                timeSpan = timeSpan - new TimeSpan(numYears * 365, 0, 0, 0);
            }

            var numWeeks = timeSpan.Days / 7;
            if (numWeeks > 0)
            {
                s += $"{numWeeks}w ";
                timeSpan = timeSpan - new TimeSpan(numWeeks * 7, 0, 0, 0);
            }

            if (timeSpan.Days > 0)
            {
                s += $"{timeSpan.Days}d ";
            }

            if (timeSpan.Hours > 0)
            {
                s += $"{timeSpan.Hours}h ";
            }

            if (timeSpan.Minutes > 0)
            {
                s += $"{timeSpan.Minutes}m ";
            }

            if (!string.IsNullOrEmpty(s))
            {
                if (timeSpan.Seconds > 0)
                {
                    s += $"{timeSpan.Seconds}s";
                }

                return s.Trim();
            }

            if (timeSpan.Seconds > 0)
            {
                s += $"{timeSpan.Seconds}s ";
            }

            var remainingTicks = timeSpan.Ticks - (timeSpan.Seconds * 10_000_000);
            var milliseconds = remainingTicks / 10_000;

            if (milliseconds > 0)
            {
                s += $"{milliseconds}ms";
            }

            return s.Trim();
        }

        public static string ConvertBytesForDisplay(this long bytes)
        {
            const float OneKb = 1024;
            const float OneMb = 1024 * 1024;
            const float OneGb = 1024 * 1024 * 1024;
            string convertedBytes;

            if (bytes > OneGb)
            {
                convertedBytes = $"{bytes / OneGb:#.##} GB";
            }
            else if (bytes > OneMb)
            {
                convertedBytes = $"{bytes / OneMb:#.##} MB";
            }
            else if (bytes > OneKb)
            {
                convertedBytes = $"{bytes / OneKb:#.##} KB";
            }
            else
            {
                convertedBytes = $"{bytes} bytes";
            }

            return convertedBytes;
        }

        
    }
}
