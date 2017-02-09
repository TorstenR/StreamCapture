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
            //make filelist
            string concatList=files.fileCaptureList[0].GetFullFile();
            for(int i=1;i<files.fileCaptureList.Count;i++)
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

        public void MuxFile(string metadata)
        {
            //Get the right input file
            VideoFileInfo inputFile;
            if(files.numberOfFiles>1)
                inputFile=files.muxedFile;
            else
                inputFile=files.fileCaptureList[0];

            //Set
            files.SetMuxedFile(configuration["outputPath"]);

            // "muxCmdLine": "[FULLFFMPEGPATH] -i [VIDEOFILE] -acodec copy -vcodec copy [FULLOUTPUTPATH]"
            string cmdLineArgs = configuration["muxCmdLine"];
            cmdLineArgs=cmdLineArgs.Replace("[VIDEOFILE]",inputFile.GetFullFile());
            cmdLineArgs=cmdLineArgs.Replace("[FULLOUTPUTPATH]",files.muxedFile.GetFullFile());
            cmdLineArgs=cmdLineArgs.Replace("[DESCRIPTION]",metadata);            

            //Run command
            logWriter.WriteLine($"{DateTime.Now}: Starting Mux: {configuration["ffmpegPath"]} {cmdLineArgs}");
            new ProcessManager(configuration).ExecProcess(logWriter,configuration["ffmpegPath"],cmdLineArgs);
        }

        public void PublishAndCleanUpAfterCapture()
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
    }
}