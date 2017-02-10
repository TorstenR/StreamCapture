using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;

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

        public void DeleteNonPublishedFiles(TextWriter logWriter,IConfiguration configuration)
        {
            //Make sure we have a published file
            if(!File.Exists(publishedfile.GetFullFile()))
            {
                logWriter.WriteLine($"{DateTime.Now}: ERROR: Not published {publishedfile.GetFullFile()}");
                new Mailer().SendErrorMail(configuration,"Not Published!",string.Format($"{publishedfile.GetFullFile()} was not published on {DateTime.Now}"));
                return;
            }

            //Delete captured files
            foreach(VideoFileInfo fileInfo in fileCaptureList)
            {
                if(File.Exists(fileInfo.GetFullFile()))
                {
                    logWriter.WriteLine($"{DateTime.Now}: Deleting file {fileInfo.GetFullFile()}");
                    File.Delete(fileInfo.GetFullFile());
                }
            }

            //Delete concat file
            if(numberOfFiles>1)
            {
                logWriter.WriteLine($"{DateTime.Now}: Deleting file {concatFile.GetFullFile()}");
                File.Delete(concatFile.GetFullFile());
            }

            //Delete mux file
            logWriter.WriteLine($"{DateTime.Now}: Deleting file {muxedFile.GetFullFile()}");
            File.Delete(muxedFile.GetFullFile());
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
                baseFileName=fileName+"_concat",
                exten=".ts",
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
