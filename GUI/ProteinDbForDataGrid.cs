using System;
using System.Collections.Generic;
using System.Text;
using Tasks;

namespace GUI
{
    internal class ProteinDbForDataGrid
    {
        #region Public Constructors

        public ProteinDbForDataGrid(string FilePath)
        {
            Use = true;
            this.FilePath = FilePath;
            if (FilePath.ToUpperInvariant().Contains("contaminant".ToUpperInvariant())
                || FilePath.ToUpperInvariant().Contains("CRAP"))
            {
                Contaminant = true;
            }
            FileName = System.IO.Path.GetFileName(FilePath);
        }

        public ProteinDbForDataGrid(DbForDigestion uu)
        {
            Use = true;            
            FilePath = uu.FilePath;
            FileName = uu.FileName;
        }

        #endregion Public Constructors

        #region Public Properties

        public bool Use { get; set; }
        public bool Contaminant { get; set; }
        public string FileName { get; private set; }
        public string FilePath { get; private set; }
        public bool InProgress { get; private set; }

        #endregion Public Properties

        #region Public Methods

        /// <summary>
        /// Method to mark as in progress. Need the property setter to be private so user could not check off in GUI
        /// </summary>
        /// <param name="inProgress"></param>
        public void SetInProgress(bool inProgress)
        {
            InProgress = inProgress;
        }

        #endregion Public Methods
    }
}
