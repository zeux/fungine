@echo off
setlocal

rem Checkout sources
call svn checkout http://slimdx.googlecode.com/svn/trunk/build/ slimdx/build
call svn checkout http://slimdx.googlecode.com/svn/trunk/external/Effects11 slimdx/external/Effects11
call svn checkout http://slimdx.googlecode.com/svn/trunk/source slimdx/source

rem Apply patch:
rem 1. All components except D3D11-related ones and RawInput are stripped
rem 2. Resources and custom exception error messages (dxerr.lib) are stripped
rem 3. ObjectTable is removed, all objects now have finalizers (no need to manually dispose everything)
patch -p0 -d slimdx <SlimDX.diff

rem Build solution
for /f "usebackq tokens=3" %%i in (`reg query HKLM\Software\Microsoft\MSBuild\ToolsVersions\4.0 /v MSBuildToolsPath`) do set MSBUILDPATH=%%i
%MSBUILDPATH%\msbuild SlimDX.sln /nologo /verbosity:quiet /p:configuration=Release /p:platform=Win32 || exit %ERRORLEVEL%

rem Copy results
copy /y Release\SlimDX.dll SlimDX.dll

rem Delete artefacts
rmdir /s /q Release
rmdir /s /q SlimDX
