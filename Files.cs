using System.Collections.Generic;
using System.IO;

namespace StreamCapture
{
    public class Files
    {
        public string fileName { set; get; }
        public List<FileInfo> fileCaptureList { set; get; }
        public FileInfo concatFile { set; get; }
        public FileInfo muxedFile { set; get; }
        public FileInfo publishedfile { set; get; }

        public int numberOfFiles { set; get; }

        public Files(string _fileName)
        {
            fileName=_fileName;
            numberOfFiles=0;
            fileCaptureList = new List<FileInfo>();
        }

        public void AddCaptureFile(string _baseFilePath,int _fileNumber)
        {
            FileInfo fileInfo=new FileInfo
            {
                baseFileName=fileName,
                exten=".ts",
                fileNumber=_fileNumber,
                baseFilePath=_baseFilePath     
            };

            //Increment file count
            numberOfFiles++;

            //Make sure file doesn't already exist
            CheckForDup(fileInfo);

            //Add to list
            fileCaptureList.Add(fileInfo);
        }

        public void DeleteCapturedFiles()
        {
            foreach(FileInfo fileInfo in fileCaptureList)
            {
                File.Delete(fileInfo.GetFullFile());
            }
        }

        private void CheckForDup(FileInfo fileInfo)
        {
            //Make sure file doesn't already exist
            if(File.Exists(fileInfo.GetFullFile()))
            {
                FileInfo newFileInfo = CloneFileInfo(fileInfo);
                newFileInfo.RandomizeFileName();
                File.Move(fileInfo,newFileInfo);          
                fileInfo=newFileInfo;
            }
        }

        private FileInfo CloneFileInfo(FileInfo origFileInfo)
        {
            return new FileInfo
            {
                baseFileName=origFileInfo.baseFileName,
                fileNumber=origFileInfo.fileNumber,
                exten=origFileInfo.exten,
                baseFilePath=origFileInfo.baseFilePath
            };
        }

        public void SetConcatFile(string _baseFilePath)
        {
            muxedFile=new FileInfo
            {
                baseFileName=fileName,
                exten=".concat",
                baseFilePath=_baseFilePath     
            };

            CheckForDup(muxedFile);
        }

        public void SetMuxedFile(string _baseFilePath)
        {
            muxedFile=new FileInfo
            {
                baseFileName=fileName,
                exten=".mp4",
                baseFilePath=_baseFilePath     
            };

            CheckForDup(muxedFile);
        }

        public void SetPublishedFile(string _baseFilePath)
        {
            publishedfile=new FileInfo
            {
                baseFileName=fileName,
                exten=".mp4",
                baseFilePath=_baseFilePath     
            };

            CheckForDup(publishedfile);
        }
    }
}
