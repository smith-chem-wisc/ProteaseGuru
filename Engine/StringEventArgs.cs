using System;
using System.Collections.Generic;
using System.Text;

namespace ProteaseGuru.Engine
{
    public class StringEventArgs : MyRecursiveEventArgs
    {
        public StringEventArgs(string s, List<string> nestedIDs)
            : base(nestedIDs)
        {
            this.S = s;
        }

        public string S { get; }
    }
}
