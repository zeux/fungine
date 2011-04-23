@echo off
setlocal

rem Checkout sources
call svn checkout --depth=files http://snappy.googlecode.com/svn/trunk/ snappy

rem Build solution
for /f "usebackq tokens=3" %%i in (`reg query HKLM\Software\Microsoft\MSBuild\ToolsVersions\4.0 /v MSBuildToolsPath`) do set MSBUILDPATH=%%i
%MSBUILDPATH%\msbuild snappy.sln /nologo /verbosity:quiet /p:configuration=Release /p:platform=Win32 || exit %ERRORLEVEL%

rem Copy results
copy /y Release\snappy.dll snappy.dll

rem Delete artefacts
rmdir /s /q Release
rmdir /s /q snappy
