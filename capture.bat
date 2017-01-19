@echo off 
set channel=%1
set time=%2
set filename=%3
set nasvidepath=\\nas\home\video\live247

REM 
REM Capture stream itself
REM
streamCapture -c %channel% -d %time% -f %filename% > %filename%Log.txt 2> %filename%Ffmpeglog.txt

