@echo off
set _OPTIONS_CONFIG=Release
set _OPTIONS_PLATFORM=x86

for /f "usebackq tokens=3" %%i in (`reg query HKLM\Software\Microsoft\MSBuild\ToolsVersions\4.0 /v MSBuildToolsPath`) do set MSBUILDPATH=%%i
%MSBUILDPATH%\msbuild src/fungine.sln /nologo /verbosity:quiet /p:configuration=%_OPTIONS_CONFIG% /p:platform=%_OPTIONS_PLATFORM% || exit %ERRORLEVEL%

bin\%_OPTIONS_CONFIG%_%_OPTIONS_PLATFORM%\fungine
