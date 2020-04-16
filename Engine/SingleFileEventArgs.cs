using System;
using System.Collections.Generic;
using System.Text;

namespace Engine
{    public class SingleFileEventArgs : MyRecursiveEventArgs
    {
        public SingleFileEventArgs(string writtenFile, List<string> nestedIds) : base(nestedIds)
        {
            WrittenFile = writtenFile;
        }

        public string WrittenFile { get; private set; }
    }
}
