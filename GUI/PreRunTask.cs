using System;
using System.Collections.Generic;
using System.Text;
using Tasks;

namespace GUI
{
    internal class PreRunTask
    {
        #region Public Fields

        public readonly ProteaseGuruTask proteaseGuruTask;

        #endregion Public Fields

        #region Public Constructors

        public PreRunTask(ProteaseGuruTask theTask)
        {
            proteaseGuruTask = theTask;
        }

        #endregion Public Constructors

        #region Public Properties

        public string DisplayName { get; set; }

        #endregion Public Properties

        #region Public Methods

        public PreRunTask Clone()
        {
            return (PreRunTask)this.MemberwiseClone();
        }

        #endregion Public Methods
    }
}
