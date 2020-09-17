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
        public VideoFileInfo publishedFile { set; get; }
        public VideoFileInfo posterFile { set; get; }
        public VideoFileInfo fanartFile { set; get; }

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
            if(!File.Exists(publishedFile.GetFullFile()))
            {
                logWriter.WriteLine($"{DateTime.Now}: ERROR: Not published {publishedFile.GetFullFile()}");
                new Mailer().SendErrorMail(configuration,"Not Published!",string.Format($"{publishedFile.GetFullFile()} was not published on {DateTime.Now}"));
                return;
            }

            //Delete captured files
            foreach(VideoFileInfo fileInfo in fileCaptureList)
            {
                if(File.Exists(fileInfo.GetFullFile()))
                {
                    DeleteFile(logWriter,fileInfo);
                }
            }

            //Delete concat file
            if(numberOfFiles>1)
            {
                logWriter.WriteLine($"{DateTime.Now}: Deleting file {concatFile.GetFullFile()}");
                File.Delete(concatFile.GetFullFile());
            }
        }

        public void DeletePublishedFiles(TextWriter logWriter,IConfiguration configuration)
        {
            DeleteFile(logWriter,posterFile);
            DeleteFile(logWriter,fanartFile);
            DeleteFile(logWriter,publishedFile);
        }

        private void DeleteFile(TextWriter logWriter,VideoFileInfo fileInfo)
        {
            int tryNumber = 0;

            logWriter.WriteLine($"{DateTime.Now}: Deleting file {fileInfo.GetFullFile()}");
            while (true)
            {
                try
                {
                    File.Delete(fileInfo.GetFullFile());
                    break;
                }
                catch (System.IO.IOException ex)
                {
                    if (tryNumber<13)  //file in use...  (ex.HResult == -2147024864 && )
                    {
                        logWriter.WriteLine($"{DateTime.Now}: File {fileInfo.GetFullFile()} is in use.  Waiting 15 minutes before trying again...  (Try {tryNumber+1} of 13)");
                        System.Threading.Thread.Sleep(15*(60*1000));
                        tryNumber++;
                    }
                    else
                    {
                        throw ex;
                    }
                }
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
                baseFileName=fileName+"_concat",
                exten=".mpg",
                baseFilePath=_baseFilePath     
            };

            CheckForDup(concatFile);
        }

        public void SetMuxedFile(string _baseFilePath)
        {
            //Take care of video file
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
            publishedFile=new VideoFileInfo
            {
                baseFileName=fileName,
                exten=".mp4",
                baseFilePath=_baseFilePath     
            };
            CheckForDup(publishedFile);

            //Now create poster and fanart stills
            posterFile = new VideoFileInfo
            {
                baseFileName = fileName,
                exten = ".jpg",
                baseFilePath = _baseFilePath
            };
            CheckForDup(posterFile);

            fanartFile = new VideoFileInfo
            {
                baseFileName = fileName + "-fanart",
                exten = ".jpg",
                baseFilePath = _baseFilePath
            };
            CheckForDup(fanartFile);
        }
    }
}
