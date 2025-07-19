using System;
using System.Windows.Threading;

namespace GUI
{
    class SearchModifications
    {
        public static DispatcherTimer Timer;

        public static void SetUp()
        {
            Timer = new DispatcherTimer();
            Timer.Interval = TimeSpan.FromMilliseconds(300);
        }

        // starts timer to keep track of user keystrokes
        public static void SetTimer()
        {
            // Reset the timer
            Timer.Stop();
            Timer.Start();
        }
    }
}