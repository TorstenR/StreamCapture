
using System.IO;

namespace StreamCapture
{
    public class FileInfo
    {
        public string baseFileName { get; set; }
        public int numberOfFiles { get; set; }
        public string baseFilePath { get; set; }
        public int currentFileNumber { get; set; }
        private string exten { get; set; }

        public FileInfo(string _exten)
        {
            numberOfFiles=0;
            currentFileNumber=0;
            exten=_exten;
        }

        public string GetFullFileWithPath()
        {
            string fullFilePath=Path.Combine(baseFilePath,baseFileName);
            if(!publishMode)
            {
                fullFilePath=fullFilePath+currentFileNumber+captureExten;
            }
            else
            {
                fullFilePath=fullFilePath+publishExten;
            }
            return fullFilePath;
        }
    }
}