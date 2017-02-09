using System.Collections.Generic;
using System.IO;

namespace StreamCapture
{
    public class VideoFiles
    {
        public string fileName { set; get; }
        public List<VideoFileInfo> fileCaptureList { set; get; }
        public VideoFileInfo concatFile { set; get; }
        public VideoFileInfo muxedFile { set; get; }
        public VideoFileInfo publishedfile { set; get; }

        public int numberOfFiles { set; get; }

        public VideoFiles(string _fileName)
        {
            fileName=_fileName;
            numberOfFiles=0;
            fileCaptureList = new List<VideoFileInfo>();
        }
        public VideoFileInfo AddCaptureFile(string _baseFilePath)
        {
            VideoFileInfo fileInfo=new VideoFileInfo
            {
                baseFileName=fileName,
                exten=".ts",
                fileNumber=numberOfFiles,
                baseFilePath=_baseFilePath
            };

            //Increment file count
            numberOfFiles++;

            //Make sure file doesn't already exist
            CheckForDup(fileInfo);

            //Add to list
            fileCaptureList.Add(fileInfo);

            return fileInfo;
        }

        public void DeleteCapturedFiles()
        {
            foreach(VideoFileInfo fileInfo in fileCaptureList)
            {
                File.Delete(fileInfo.GetFullFile());
            }
        }

        private void CheckForDup(VideoFileInfo fileInfo)
        {
            //Make sure file doesn't already exist
            if(File.Exists(fileInfo.GetFullFile()))
            {
                VideoFileInfo newFileInfo = CloneFileInfo(fileInfo);
                newFileInfo.RandomizeFileName();
                File.Move(fileInfo.GetFullFile(),newFileInfo.GetFullFile());          
                fileInfo=newFileInfo;
            }
        }

        private VideoFileInfo CloneFileInfo(VideoFileInfo origFileInfo)
        {
            return new VideoFileInfo
            {
                baseFileName=origFileInfo.baseFileName,
                fileNumber=origFileInfo.fileNumber,
                exten=origFileInfo.exten,
                baseFilePath=origFileInfo.baseFilePath
            };
        }

        public void SetConcatFile(string _baseFilePath)
        {
            concatFile=new VideoFileInfo
            {
                baseFileName=fileName,
                exten=".concat",
                baseFilePath=_baseFilePath     
            };

            CheckForDup(concatFile);
        }

        public void SetMuxedFile(string _baseFilePath)
        {
            muxedFile=new VideoFileInfo
            {
                baseFileName=fileName,
                exten=".mp4",
                baseFilePath=_baseFilePath     
            };

            CheckForDup(muxedFile);
        }

        public void SetPublishedFile(string _baseFilePath)
        {
            publishedfile=new VideoFileInfo
            {
                baseFileName=fileName,
                exten=".mp4",
                baseFilePath=_baseFilePath     
            };

            CheckForDup(publishedfile);
        }
    }
}
