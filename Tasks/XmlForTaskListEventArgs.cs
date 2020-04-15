using System;
using System.Collections.Generic;
using System.Text;

namespace Tasks
{
    public class XmlForTaskListEventArgs : EventArgs
    {
        public List<DbForDigestion> NewDatabases;

        public XmlForTaskListEventArgs(List<DbForDigestion> newDatabases)
        {
            NewDatabases = newDatabases;
        }
    }
}
