using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace StreamCapture
{
    public class ProcessManager
    {
        IConfiguration configuration;

        public ProcessManager(IConfiguration _c)
        {
            configuration=_c;
        }

        public CaptureProcessInfo ExecProcess(TextWriter logWriter,string exe,string cmdLineArgs)
        {
            return ExecProcess(logWriter,exe,cmdLineArgs,0,null,new CancellationTokenSource().Token);
        }

        public CaptureProcessInfo ExecProcess(TextWriter logWriter,string exe,string cmdLineArgs,int timeout,string outputPath,CancellationToken cancellationToken)
        {
            //Create our process
            var processInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = cmdLineArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            Process process = Process.Start(processInfo);

            //Let's build a timer to kill the process when done
            CaptureProcessInfo captureProcessInfo=null;
            Timer captureTimer=null;
            if(timeout>0)
            {
                int interval=10;  //# of seconds between timer/file checks
                long acceptableRate=Convert.ToInt32(configuration["acceptableRate"]);

                //create capture process info
                DateTime timerDone=DateTime.Now.AddMinutes(timeout);
                captureProcessInfo = new CaptureProcessInfo(process,acceptableRate,interval,timerDone,outputPath,logWriter,cancellationToken);

                //create timer
                TimeSpan intervalTime = new TimeSpan(0, 0, interval); 
                logWriter.WriteLine($"{DateTime.Now}: Settting Timer for {timeout} minutes in the future to kill process.");
                captureTimer = new Timer(OnCaptureTimer, captureProcessInfo, intervalTime, intervalTime);
            }

            //Now, let's wait for the thing to exit
            logWriter.WriteLine(process.StandardError.ReadToEnd());
            logWriter.WriteLine(process.StandardOutput.ReadToEnd());
            process.WaitForExit();

            //Clean up timer
            if(timeout>0 && captureTimer != null)
                captureTimer.Dispose();

            return captureProcessInfo;
        }

        //Handler for ffmpeg timer to kill the process
        static void OnCaptureTimer(object obj)
        {    
            bool killProcess=false;
            CaptureProcessInfo captureProcessInfo = obj as CaptureProcessInfo;

            //Are we done?
            if(DateTime.Now >= captureProcessInfo.timerDone)
            {
                killProcess=true;
                captureProcessInfo.logWriter.WriteLine($"{DateTime.Now}: Timer is up.  Killing capture process");
            }

            //Have we been canclled?
            if(captureProcessInfo.cancellationToken!=null && captureProcessInfo.cancellationToken.IsCancellationRequested)
            {
                killProcess=true;
                captureProcessInfo.logWriter.WriteLine($"{DateTime.Now}: Task has been cancelled.  Killing capture process");                
            }

            //Make sure file is still growing at a reasonable pace.  Otherwise, kill the process
            if(!killProcess)
            {
                //Grab file info
                FileInfo fileInfo=new FileInfo(captureProcessInfo.outputPath);

                //Make sure file even exists!
                if(!fileInfo.Exists)
                {
                    killProcess=true;
                    captureProcessInfo.logWriter.WriteLine($"{DateTime.Now}: ERROR: File {captureProcessInfo.outputPath} doesn't exist.  Feed is bad.");
                }
                else
                {
                    //Make sure file size (rate) is fine
                    long fileSize = fileInfo.Length;
                    long kBytesSec = ((fileSize-captureProcessInfo.fileSize)/captureProcessInfo.interval)/1000;
                    if(kBytesSec <= captureProcessInfo.acceptableRate)
                    {
                        killProcess=true;
                        captureProcessInfo.logWriter.WriteLine($"{DateTime.Now}: ERROR: File size no longer growing. (Current Rate: ({kBytesSec} KB/s)  Killing capture process.");
                    }
                    captureProcessInfo.fileSize=fileSize;
                    captureProcessInfo.avgKBytesSec=(captureProcessInfo.avgKBytesSec+kBytesSec)/2;
                }
            }

            //Kill process if needed
            Process p = captureProcessInfo.process;
            if(killProcess && p!=null && !p.HasExited)
            {
                p.Kill();
                p.WaitForExit();
            }
        }        
    }
}