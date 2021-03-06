@echo off
setlocal

rem Checkout sources
call svn checkout http://nvidia-texture-tools.googlecode.com/svn/trunk/src nvtt/src
call svn checkout http://nvidia-texture-tools.googlecode.com/svn/trunk/extern/poshlib nvtt/extern/poshlib
call svn checkout http://nvidia-texture-tools.googlecode.com/svn/trunk/extern/stb nvtt/extern/stb

rem Build solution
for /f "usebackq tokens=3" %%i in (`reg query HKLM\Software\Microsoft\MSBuild\ToolsVersions\4.0 /v MSBuildToolsPath`) do set MSBUILDPATH=%%i
%MSBUILDPATH%\msbuild nvtt.sln /nologo /verbosity:quiet /p:configuration=Release /p:platform=Win32 || exit %ERRORLEVEL%

rem Copy results
copy /y Release\nvtt.dll nvtt.dll

rem Delete artefacts
rmdir /s /q Release
rmdir /s /q nvtt
