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
            //Concat if more than one file
            int numFiles=files.GetNumberOfFiles();
            logWriter.WriteLine($"{DateTime.Now}: Num Files: {numFiles}");
            if(numFiles > 0)
            {
                //make filelist
                List<FileInfo> fileInfoList = files.GetFileInfoList();
                string concatList=fileInfoList[0].GetFullFileWithPath();
                for(int i=1;i<=numFiles;i++)
                    concatList=concatList+"|"+fileInfoList[i].GetFullFileWithPath();;

                //"concatCmdLine": "[FULLFFMPEGPATH] -i \"concat:[FILELIST]\" -c copy [FULLOUTPUTPATH]",
                string cmdLineArgs = configuration["concatCmdLine"];
                cmdLineArgs=cmdLineArgs.Replace("[FILELIST]",concatList);
                cmdLineArgs=cmdLineArgs.Replace("[FULLOUTPUTPATH]",outputFile);

                //Run command to concat
                logWriter.WriteLine($"{DateTime.Now}: Starting Concat: {configuration["ffmpegPath"]} {cmdLineArgs}");
                new ProcessManager(configuration).ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs);
            }
        }

        public void MuxFile(int numFiles,string fileName,string metadata)
        {
            string outputPath = configuration["outputPath"];

            //Mux file to mp4 from ts (transport stream)
            string inputFile;
            inputFile=Path.Combine(outputPath,fileName+".ts");
            string outputFile=Path.Combine(outputPath,fileName+".mp4");

            //Make sure file doesn't already exist
            if(File.Exists(outputFile))
            {
                string newFileName=Path.Combine(outputPath,fileName+"_"+Path.GetRandomFileName()+".mp4");
                File.Move(outputFile,newFileName);                 
            }

            // "muxCmdLine": "[FULLFFMPEGPATH] -i [VIDEOFILE] -acodec copy -vcodec copy [FULLOUTPUTPATH]"
            string cmdLineArgs = configuration["muxCmdLine"];
            cmdLineArgs=cmdLineArgs.Replace("[VIDEOFILE]",inputFile);
            cmdLineArgs=cmdLineArgs.Replace("[FULLOUTPUTPATH]",outputFile);
            cmdLineArgs=cmdLineArgs.Replace("[DESCRIPTION]",metadata);            

            //Run command
            logWriter.WriteLine($"{DateTime.Now}: Starting Mux: {configuration["ffmpegPath"]} {cmdLineArgs}");
            new ProcessManager(configuration).ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs);
        }

        public void PublishAndCleanUpAfterCapture(int numFiles,string fileName)
        {
            string outputPath = configuration["outputPath"];

            string outputFile=Path.Combine(outputPath,fileName+".mp4");

            //If final file exist, delete old .ts file/s
            if(File.Exists(outputFile))
            {
                string inputFile=Path.Combine(outputPath,fileName+".ts");
                logWriter.WriteLine($"{DateTime.Now}: Removing ts file: {inputFile}");
                File.Delete(inputFile);

                if(numFiles>0)
                {
                    for(int i=1;i<numFiles;i++)
                    {
                        inputFile=Path.Combine(outputPath,fileName+i+".ts");
                        logWriter.WriteLine($"{DateTime.Now}: Removing ts file: {inputFile}");
                        File.Delete(inputFile);
                    }
                }
            }

            //If NAS path exists, move file mp4 file there
            string nasPath = configuration["nasPath"];
            if(nasPath != null)
            {
                string nasFile=Path.Combine(nasPath,fileName+".mp4");
                if(File.Exists(nasFile))
                {
                    string newFileName=Path.Combine(configuration["outputPath"],fileName+"_"+Path.GetRandomFileName()+".mp4");
                    File.Move(outputPath,newFileName);
                }

                logWriter.WriteLine($"{DateTime.Now}: Moving {outputFile} to {nasFile}");
                File.Move(outputFile,nasFile);
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