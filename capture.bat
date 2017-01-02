@echo off 
set channel=%1
set time=%2
set filename=%3
set vidpath=D:\Video\247tvCapture
set nasvidepath=\\nas\home\video\live247

REM 
REM Capture stream itself
REM
livecapture %channel% %time% %vidpath%\%filename% > %vidpath%\log.txt 2> %vidpath%\ffmpeglog.txt

REM Create list of videos. There may have been multiple given interruptions
del %vidpath%\mylist.txt
(for /F %%i in ('dir /b/d/o:d %vidpath%\%filename%*.ts') do (
  @echo file '%vidpath%\%%i')) > %vidpath%\mylist.txt

REM Concatenate the videos and clean up afterwards if there were no errors
ffmpeg\bin\ffmpeg -f concat -safe 0 -i %vidpath%\mylist.txt -c copy %vidpath%\tempoutput.ts > %vidpath%\concatLog.txt 2> %vidpath%\concatFfmpeglog.txt
IF %ERRORLEVEL% EQU 0 (
   del %vidpath%\%filename%*.ts
   REM move %vidpath%\%filename%*.ts %vidpath%\raw
   ren %vidpath%\tempoutput.ts %filename%.ts
  
   REM move %vidpath%\%filename%*.ts %nasvidepath%
   copy %vidpath%\%filename%*.ts %nasvidepath%

   ffmpeg\bin\ffmpeg -i %vidpath%\%filename%.ts -vcodec libx264 -profile:v high -level 4.1 -preset superfast -movflags faststart %vidpath%\%filename%_mp4.mp4 > %vidpath%\transCodeLog.txt 2> %vidpath%\transCodeFfmpeglog.txt
   move %vidpath%\%filename%*.mp4 %nasvidepath%
)

