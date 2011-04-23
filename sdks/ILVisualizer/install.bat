@echo off
setlocal

rem Get 'my documents' path
for /f "tokens=2* skip=2" %%i in ('reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders" /v personal') do set DOCUMENTS=%%j
for /f "tokens=*" %%i in ('echo %DOCUMENTS%') do set DOCUMENTS=%%i

rem Copy visualizer files to Visual Studio path
copy *.dll "%DOCUMENTS%\Visual Studio 2010\Visualizers\"
