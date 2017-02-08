using System.Collections.Generic;

namespace StreamCapture
{
    public class Files
    {
        private List<FileInfo> fileInfoList;

        public Files()
        {
            fileInfoList = new List<FileInfo>();
        }

        public void AddFileInfo(FileInfo fileInfo)
        {
            fileInfoList.Add(fileInfo);
        }

        public int GetNumberOfFiles()
        {
            return fileInfoList.Count;
        }

        public bool GetFileInfo(int index)
        {
            return fileInfoList;
        }
    }
}