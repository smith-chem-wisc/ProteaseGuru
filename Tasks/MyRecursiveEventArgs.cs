using System;
using System.Collections.Generic;
using System.Text;

namespace Tasks
{
    public class MyRecursiveEventArgs : EventArgs
    {
        public readonly List<string> NestedIDs;

        public MyRecursiveEventArgs(List<string> nestedIDs)
        {
            this.NestedIDs = nestedIDs;
        }
    }
}
