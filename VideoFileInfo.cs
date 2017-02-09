
using System.IO;

namespace StreamCapture
{
    public class VideoFileInfo
    {
        public string baseFileName { get; set; }
        public int fileNumber { get; set; }
        public string exten { get; set; }
        public string baseFilePath { get; set; }

        public string GetFullFile()
        {
            string fullFilePath=Path.Combine(baseFilePath,baseFileName);
            if(fileNumber>0)
                fullFilePath=fullFilePath+fileNumber+exten;
            else
                fullFilePath=fullFilePath+exten;

            return fullFilePath;
        }

        public void RandomizeFileName()
        {
            baseFileName=baseFileName+Path.GetRandomFileName();
        }
    }
}