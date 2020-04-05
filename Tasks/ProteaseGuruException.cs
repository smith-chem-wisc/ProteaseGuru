using System;
using System.Collections.Generic;
using System.Text;

namespace Tasks
{
    [Serializable]
    class ProteaseGuruException : Exception
    {
        public ProteaseGuruException(string message) : base(message)
        {
        }
    }
}
