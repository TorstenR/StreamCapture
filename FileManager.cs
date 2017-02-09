using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace StreamCapture
{
    public class FileManager
    {
        IConfiguration configuration;
        TextWriter logWriter;
        public FileManager(IConfiguration _c,TextWriter _lw)
        {
            configuration=_c;
            logWriter=_lw;
        }

        public void ConcatFiles(Files files)
        {
            //make filelist
            string concatList=files.fileCaptureList[0].GetFullFile();
            for(int i=1;i<=numFiles;i++)
                concatList=concatList+"|"+files.fileCaptureList[i];

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

        public void MuxFile(Files files,string metadata)
        {
            //Get the right input file
            FileInfo inputFile;
            if(files.numberOfFiles>1)
                inputFile=files.muxedFile;
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

        public void PublishAndCleanUpAfterCapture(Files files)
        {
            //If NAS path exists, move file mp4 file there
            string nasPath = configuration["nasPath"];
            if(nasPath != null)
            {
                files.SetPublishedFile(configuration["nasPath"]);
                logWriter.WriteLine($"{DateTime.Now}: Moving {files.muxedFile.GetFullFile()} to {files.publishedfile.GetFullFile()}");
                File.Move(files.muxedFile.GetFullFile(),files.publishedfile.GetFullFile());
            }

            //If final file exist, delete old .ts file/s
            if(File.Exists(files.muxedFile.GetFullFile()))
            {
                files.DeleteCapturedFiles();
            }
        }

        public void CleanOldFiles()
        {
            string logPath = configuration["logPath"];
            string outputPath = configuration["outputPath"];
            string nasPath = configuration["nasPath"];
            int retentionDays = Convert.ToInt16(configuration["retentionDays"]);
            
            DateTime cutDate=DateTime.Now.AddDays(retentionDays*-1);
            Console.WriteLine($"{DateTime.Now}: Checking for files older than {cutDate}");

            try
            {
                RemoveOldFiles(logPath,cutDate);
                RemoveOldFiles(outputPath,cutDate);
                if(!string.IsNullOrEmpty(nasPath))
                    RemoveOldFiles(nasPath,cutDate);
            }
            catch(Exception e)
            {
                Console.WriteLine($"{DateTime.Now}: ERROR: Problem cleaning up old files.  Error: {e.Message}");
            }
        }

        private void RemoveOldFiles(string path,DateTime asOfDate)
        {
            string[] fileList=Directory.GetFiles(path);
            foreach(string file in fileList)
            {
                if(File.GetCreationTime(file) < asOfDate)
                {
                    Console.WriteLine($"{DateTime.Now}: Removing old file {file} as it is too old  ({File.GetCreationTime(file)})");
                    File.Delete(file);
                }
            }
        }
    }
}