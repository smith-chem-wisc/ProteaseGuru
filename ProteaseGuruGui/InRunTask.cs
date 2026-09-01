using System;
using System.Collections.Generic;
using System.Text;
using ProteaseGuru.Tasks;

namespace ProteaseGuru.Gui
{
    public class InRunTask : ForTreeView
    {
        public readonly ProteaseGuruTask Task;

        public InRunTask(string displayName, ProteaseGuruTask task) : base(displayName, displayName)
        {
            Task = task;
        }
    }
}
