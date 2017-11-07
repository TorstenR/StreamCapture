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
            if(string.IsNullOrEmpty(configuration["nasPath"]))
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

        //Move published files to storage end point in the cloud if available
        public void MoveToEndPoint(string category)
        {
            //If could end point path exists, move file mp4 file there          
            if(string.IsNullOrEmpty(configuration["storageEndPoint"]) || string.IsNullOrEmpty(configuration["netCopyCmdLine"]))
                return;

            logWriter.WriteLine($"{DateTime.Now}: Copying files to the storage end point now");

            //Grab storage end point info
            string endPoint = configuration["storageEndPoint"];
            string destPath = $"{endPoint}/{category}";

            //Move files 
            MoveToEndPoint(files.posterFile,destPath);
            MoveToEndPoint(files.fanartFile,destPath);
            MoveToEndPoint(files.publishedFile,destPath);

            //Remove published files since we've moved them to the cloud
            files.DeletePublishedFiles(logWriter,configuration);
        }

        private void MoveToEndPoint(VideoFileInfo fileInfo,string destPath)
        {
            int tryNumber =0;

            while (true)
            {
                string cmdLineArgs = configuration["netCopyCmdLine"];
                cmdLineArgs = cmdLineArgs.Replace("[SOURCEPATH]", fileInfo.baseFilePath );
                cmdLineArgs = cmdLineArgs.Replace("[DESTPATH]", destPath);
                cmdLineArgs = cmdLineArgs.Replace("[FILENAME]", $"{fileInfo.baseFileName+fileInfo.exten}");
                logWriter.WriteLine($"{DateTime.Now}: Moving {fileInfo.baseFileName} to Storage Endpoint: {cmdLineArgs}");
                int exitCode = new ProcessManager(configuration).ExecProcess(logWriter, configuration["azCopy"], cmdLineArgs);  
                if(exitCode != 0)              
                { 
                    if (tryNumber<5) 
                    {
                        logWriter.WriteLine($"{DateTime.Now}: Unable to copy {fileInfo.baseFileName+fileInfo.exten} to the web end point.  Waiting 5 seconds before trying again...  (Try {tryNumber+1} of 5)");
                        System.Threading.Thread.Sleep(5*1000);
                        tryNumber++;
                    }
                    else
                    {
                        throw new Exception($"Unable to copy {fileInfo.baseFileName+fileInfo.exten} to the web end point");
                    }
                }
                else
                {
                    break;
                }
            }    
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
                RemoveOldFiles(outputPath,"*.jpg",cutDate);
                if(!string.IsNullOrEmpty(nasPath))
                {
                    Console.WriteLine($"{DateTime.Now}:          {nasPath}");
                    //Go throw sub directories too
                    RemoveOldFiles(nasPath,"*.mp4",cutDate);
                    RemoveOldFiles(nasPath,"*.jpg",cutDate);
                    string[] subDirs=Directory.GetDirectories(nasPath);
                    foreach(string subDir in subDirs)
                    {
                        Console.WriteLine($"{DateTime.Now}:          {subDir}");
                        RemoveOldFiles(subDir,"*.mp4",cutDate);
                        RemoveOldFiles(subDir,"*.jpg",cutDate);
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