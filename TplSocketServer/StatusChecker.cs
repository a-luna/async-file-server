using System;
using System.Threading;

namespace TplSockets
{
    public delegate void StatusCheckerDelegate();
    public class StatusChecker
    {
        readonly Timer _noActivityTimer;
        readonly int _noActivityInterval;

        public StatusChecker(int interval)
        {
            _noActivityInterval = interval;
            LastUpdateTime = DateTime.Now;

            _noActivityTimer = new Timer(CheckInNoActivity, true, _noActivityInterval, Timeout.Infinite);
        }

        public DateTime LastUpdateTime { get; set; }
        public event StatusCheckerDelegate NoActivityEvent;

        void CheckInNoActivity(object state)
        {
            _noActivityTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _noActivityTimer.Dispose();

            NoActivityEvent?.Invoke();
        }

        public void CheckInStatusIsActive()
        {
            LastUpdateTime = DateTime.Now;
            _noActivityTimer.Change(_noActivityInterval, Timeout.Infinite);
        }

        public void FileTransferComplete()
        {
            _noActivityTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _noActivityTimer.Dispose();
        }
    }
}
