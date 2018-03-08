namespace TplSocketServer
{
    using System;
    using System.Threading;

    public delegate void StatusCheckerDelegate();
    public class StatusChecker
    {
        private readonly Timer _noActivityTimer;

        public StatusChecker(int interval, int maxCount)
        {
            NoActivityInterval = interval;
            MaxCount = maxCount;
            NoActivityCount = 0;

            LastUpdateTime = DateTime.Now;
            _noActivityTimer = new Timer(CheckInNoActivity, true, NoActivityInterval, NoActivityInterval);
        }

        public int NoActivityInterval { get; set; }
        public int NoActivityCount { get; set; }
        public int MaxCount { get; set; }
        public DateTime LastUpdateTime { get; set; }

        public event StatusCheckerDelegate FileTransferIsDead;

        public void CheckInNoActivity(object state)
        {
            NoActivityCount++;

            if (NoActivityCount >= MaxCount)
            {
                _noActivityTimer.Dispose();
                FileTransferIsDead?.Invoke();
            }
        }

        public void CheckInTransferProgress()
        {
            LastUpdateTime = DateTime.Now;
            NoActivityCount = 0;
        }

        public void FileTransferComplete()
        {
            _noActivityTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _noActivityTimer.Dispose();
        }
    }
}
