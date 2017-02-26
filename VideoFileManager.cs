using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace StreamCapture
{
    public class VideoFileManager
    {
        IConfiguration configuration;
        TextWriter logWriter;
        VideoFiles files;
        static readonly object _lock = new object();  //used to lock file move to not overload disk
        public VideoFileManager(IConfiguration _c,TextWriter _lw,string _fn)
        {
            configuration=_c;
            logWriter=_lw;
            files = new VideoFiles(_fn);
        }

        public VideoFileInfo AddCaptureFile(string _baseFilePath)
        {
            return files.AddCaptureFile(_baseFilePath);
        }

        public int GetNumberOfFiles()
        {
            return files.numberOfFiles;
        }

        public void ConcatFiles()
        {
            //Do we need to concatenate at all?
            if(files.numberOfFiles<2)
                return;

            //make filelist
            string concatList=files.fileCaptureList[0].GetFullFile();
            for(int i=1;i<files.fileCaptureList.Count;i++)
            {
                if(File.Exists(files.fileCaptureList[i].GetFullFile()))
                    concatList=concatList+"|"+files.fileCaptureList[i].GetFullFile();
            }

            //resulting concat file
            files.SetConcatFile(configuration["outputPath"]);

            //"concatCmdLine": "[FULLFFMPEGPATH] -i \"concat:[FILELIST]\" -c copy [FULLOUTPUTPATH]",
            string cmdLineArgs = configuration["concatCmdLine"];
            cmdLineArgs=cmdLineArgs.Replace("[FILELIST]",concatList);
            cmdLineArgs=cmdLineArgs.Replace("[FULLOUTPUTPATH]",files.concatFile.GetFullFile());

            //Run command to concat
            logWriter.WriteLine($"{DateTime.Now}: Starting Concat: {configuration["ffmpegPath"]} {cmdLineArgs}");
            new ProcessManager(configuration).ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs);
        }

        public void MuxFile(string metadata)
        {
            //Set
            files.SetMuxedFile(configuration["outputPath"]);

            //If NAS path does not exist, set published file to this output too
            if(string.IsNullOrEmpty(configuration["nasPath"]))
            {
                files.SetPublishedFile(configuration["outputPath"]);
            }
            
            //Get the right input file
            VideoFileInfo inputFile;
            if(files.numberOfFiles>1)
                inputFile=files.concatFile;
            else
                inputFile=files.fileCaptureList[0];

            // "muxCmdLine": "[FULLFFMPEGPATH] -i [VIDEOFILE] -acodec copy -vcodec copy [FULLOUTPUTPATH]"
            string cmdLineArgs = configuration["muxCmdLine"];
            cmdLineArgs=cmdLineArgs.Replace("[VIDEOFILE]",inputFile.GetFullFile());
            cmdLineArgs=cmdLineArgs.Replace("[FULLOUTPUTPATH]",files.muxedFile.GetFullFile());
            cmdLineArgs=cmdLineArgs.Replace("[DESCRIPTION]",metadata);            

            //Run command
            logWriter.WriteLine($"{DateTime.Now}: Starting Mux: {configuration["ffmpegPath"]} {cmdLineArgs}");
            new ProcessManager(configuration).ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs);
        }

        public void PublishAndCleanUpAfterCapture(string category)
        {
            //If NAS path exists, move file mp4 file there
            if(!string.IsNullOrEmpty(configuration["nasPath"]))
            {
                string publishedPath=configuration["nasPath"];

                //Category passed in?  If so, let's publish to there instead
                if(!string.IsNullOrEmpty(category))
                {
                    publishedPath=Path.Combine(publishedPath,category);

                    string invalidChars = new string(Path.GetInvalidPathChars());
                    foreach (char c in invalidChars)
                        publishedPath = publishedPath.Replace(c.ToString(), "");

                    if(!Directory.Exists(publishedPath))
                        Directory.CreateDirectory(publishedPath);
                }

                //Ok, ready to publish
                files.SetPublishedFile(publishedPath);
                logWriter.WriteLine($"{DateTime.Now}: Moving {files.muxedFile.GetFullFile()} to {files.publishedfile.GetFullFile()}");
                VideoFileManager.MoveFile(files.muxedFile.GetFullFile(),files.publishedfile.GetFullFile());
            }

            //If final file exist, delete old .ts file/s
            files.DeleteNonPublishedFiles(logWriter,configuration);
        }

        static public void MoveFile(string sourcePath,string targetPath)
        {
            lock (_lock)
            {
                File.Move(sourcePath,targetPath);
            }
        }

        static public void CleanOldFiles(IConfiguration config)
        {
            string logPath = config["logPath"];
            string outputPath = config["outputPath"];
            string nasPath = config["nasPath"];
            int retentionDays = Convert.ToInt16(config["retentionDays"]);
                     
            try
            {
                DateTime cutDate=DateTime.Now.AddDays(retentionDays*-1);
                Console.WriteLine($"{DateTime.Now}: Checking the following folders for files older than {cutDate}");   
            
                Console.WriteLine($"{DateTime.Now}:          {logPath}");                
                RemoveOldFiles(logPath,"*log.txt",cutDate);
                Console.WriteLine($"{DateTime.Now}:          {outputPath}");
                RemoveOldFiles(outputPath,"*.ts",cutDate);
                RemoveOldFiles(outputPath,"*.mp4",cutDate);
                if(!string.IsNullOrEmpty(nasPath))
                {
                    Console.WriteLine($"{DateTime.Now}:          {nasPath}");
                    //Go throw sub directories too
                    RemoveOldFiles(nasPath,"*.mp4",cutDate);
                    string[] subDirs=Directory.GetDirectories(nasPath);
                    foreach(string subDir in subDirs)
                    {
                        Console.WriteLine($"{DateTime.Now}:          {subDir}");
                        RemoveOldFiles(subDir,"*.mp4",cutDate);
                    }
                }
            }
            catch(Exception e)
            {
                Console.WriteLine("======================");
                Console.WriteLine($"{DateTime.Now}: ERROR: Problem cleaning up old files.");
                Console.WriteLine("======================");
                Console.WriteLine($"{e.Message}\n{e.StackTrace}");

                //Send alert mail
                string body="Problem cleaning up old files with Exception "+e.Message;
                body=body+"\n"+e.StackTrace;
                new Mailer().SendErrorMail(config,"StreamCapture Exception! ("+e.Message+")",body);                
            }
        }

        static private void RemoveOldFiles(string path,string filter,DateTime asOfDate)
        {
            string[] fileList=Directory.GetFiles(path,filter);
            foreach(string file in fileList)
            {
                if(File.GetLastWriteTime(file) < asOfDate)
                {
                    Console.WriteLine($"{DateTime.Now}: Removing old file {file} as it is too old  ({File.GetLastWriteTime(file)})");
                    File.Delete(file);
                }
            }
        }  
    }
}