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
            string concatList="";
            bool prependPipe=false;
            for(int i=0;i<files.fileCaptureList.Count;i++)
            {
                //make sure file exist before putting it into the list
                if(File.Exists(files.fileCaptureList[i].GetFullFile()))
                {
                    if(prependPipe)
                        concatList=concatList+"|"; //prepend if not the first

                    concatList=concatList+files.fileCaptureList[i].GetFullFile();
                    prependPipe=true;  //now that we've got at least one file, we need to prepent pipe
                }
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

            //Mux file is cmdline is there, otherwise, just rename it to mp4
            if(!string.IsNullOrEmpty(configuration["muxCmdLine"]))
            {
              // "muxCmdLine": "[FULLFFMPEGPATH] -i [VIDEOFILE] -acodec copy -vcodec copy [FULLOUTPUTPATH]"
              string cmdLineArgs = configuration["muxCmdLine"];
              cmdLineArgs=cmdLineArgs.Replace("[VIDEOFILE]",inputFile.GetFullFile());
              cmdLineArgs=cmdLineArgs.Replace("[FULLOUTPUTPATH]",files.muxedFile.GetFullFile());
              cmdLineArgs=cmdLineArgs.Replace("[DESCRIPTION]",metadata);            

              //Run mux command
              logWriter.WriteLine($"{DateTime.Now}: Starting Mux: {configuration["ffmpegPath"]} {cmdLineArgs}");
              new ProcessManager(configuration).ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs);
            }
            else
            {
              VideoFileManager.MoveFile(inputFile.GetFullFile(),files.muxedFile.GetFullFile());
            }
        }

        public void PublishAndCleanUpAfterCapture(string category, int preMinutes)
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
                logWriter.WriteLine($"{DateTime.Now}: Moving {files.muxedFile.GetFullFile()} to {files.publishedFile.GetFullFile()}");
                VideoFileManager.MoveFile(files.muxedFile.GetFullFile(),files.publishedFile.GetFullFile());

                //Create poster
                string cmdLineArgs = configuration["artCmdLine"];
                cmdLineArgs = cmdLineArgs.Replace("[SECONDS]", ((preMinutes + 1) * 60).ToString());
                cmdLineArgs = cmdLineArgs.Replace("[VIDEOFILE]", files.publishedFile.GetFullFile());
                cmdLineArgs = cmdLineArgs.Replace("[FULLOUTPUTPATH]", files.posterFile.GetFullFile());
                logWriter.WriteLine($"{DateTime.Now}: Creating poster: {configuration["ffmpegPath"]} {cmdLineArgs}");
                new ProcessManager(configuration).ExecProcess(logWriter, configuration["ffmpegPath"], cmdLineArgs);

                //Create fan art
                cmdLineArgs = configuration["artCmdLine"];
                cmdLineArgs = cmdLineArgs.Replace("[SECONDS]", (((preMinutes) * 60)+15).ToString());
                cmdLineArgs = cmdLineArgs.Replace("[VIDEOFILE]", files.publishedFile.GetFullFile());
                cmdLineArgs = cmdLineArgs.Replace("[FULLOUTPUTPATH]", files.fanartFile.GetFullFile());
                logWriter.WriteLine($"{DateTime.Now}: Creating fan art: {configuration["ffmpegPath"]} {cmdLineArgs}");
                new ProcessManager(configuration).ExecProcess(logWriter, configuration["ffmpegPath"], cmdLineArgs);
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
            string tempPath = config["tempPath"];
            int retentionDays = Convert.ToInt16(config["retentionDays"]);
                     
            try
            {
                DateTime cutDate=DateTime.Now.AddDays(retentionDays*-1);
                Console.WriteLine($"{DateTime.Now}: Checking the following folders for files older than {cutDate}");   
            
                //Remove old logs
                Console.WriteLine($"{DateTime.Now}:          {logPath}");                
                RemoveOldFiles(logPath,"*log.txt",cutDate);

                //Remove old video files by walking the tree
                RemoveFilesInSubDir(tempPath,cutDate);
                RemoveFilesInSubDir(outputPath,cutDate);
                RemoveFilesInSubDir(nasPath,cutDate);
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

        static private void RemoveFilesInSubDir(string currentPath,DateTime cutDate)
        {
            Console.WriteLine($"{DateTime.Now}:          {currentPath}");

            //Remove video files
            RemoveOldFiles(currentPath,"*.mp4",cutDate);
            RemoveOldFiles(currentPath,"*.jpg",cutDate);
            RemoveOldFiles(currentPath,"*.ts",cutDate);

            //Recurse through dub directories if any
            string[] subDirs=Directory.GetDirectories(currentPath);
            foreach(string subDir in subDirs)
            {
                RemoveFilesInSubDir(subDir,cutDate);
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