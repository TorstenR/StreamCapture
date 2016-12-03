@echo off 
set channel=%1
set time=%2
set filename=%3
set vidpath=d:\video\247tvcapture

REM 
REM Capture stream itself
REM
livecapture %channel% %time% D:\Video\247tvCapture\%filename% > log.txt 2> ffmpeglog.txt

REM Create list of videos. There may have been multiple given interruptions
del %vidpath%\mylist.txt
(for /F %%i in ('dir /B/D %vidpath%\%filename%*.ts') do (
  @echo file '%vidpath%\%%i')) > %vidpath%\mylist.txt

REM Concatenate the videos and clean up afterwards if there were no errors
ffmpeg\bin\ffmpeg -f concat -safe 0 -i %vidpath%\mylist.txt -c copy %vidpath%\tempoutput.ts > log2.txt 2> ffmpeglog2.txt
IF %ERRORLEVEL% EQU 0 (
   del %vidpath%\%filename%*.ts
   ren %vidpath%\tempoutput.ts %filename%.ts
)

