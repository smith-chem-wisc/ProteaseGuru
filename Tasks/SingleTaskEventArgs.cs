using System;
using System.Collections.Generic;
using System.Text;

namespace Tasks
{
    public class SingleTaskEventArgs : EventArgs
    {
        public SingleTaskEventArgs(string displayName)
        {
            this.DisplayName = displayName;
        }

        public string DisplayName { get; private set; }
    }
}
