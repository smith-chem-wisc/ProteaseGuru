using System;
using System.Collections.Generic;
using System.Text;

namespace Tasks
{
    public class DbForDigestion
    {
        public string FilePath { get; }
        public string FileName { get; }
        public DbForDigestion(string filePath)
        {
            FilePath = filePath;
            FileName = System.IO.Path.GetFileName(filePath);
        }
    }
}
