using System;
using System.Collections.Generic;
using System.Text;

namespace ProteaseGuru.Engine
{
    [Serializable]
    public class ProteaseGuruException : Exception
    {
        public ProteaseGuruException(string message) : base(message)
        {
        }
    }
}
