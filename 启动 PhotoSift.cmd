@echo off
setlocal
rem PhotoSift: builds the helper with the C# compiler that ships with Windows, then starts it.
rem The helper is rebuilt whenever Server.cs has changed since the last build (after an update or an edit).
title PhotoSift
cd /d "%~dp0PhotoSift-helper"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
rem PhotoSift.build holds the size and time of the Server.cs that PhotoSift.exe was built from.
set "SRC="
set "BUILT="
for %%F in (Server.cs) do set "SRC=%%~zF %%~tF"
if exist PhotoSift.exe if exist PhotoSift.build set /p BUILT=<PhotoSift.build
if "%BUILT%"=="%SRC%" goto run
echo Preparing PhotoSift...
"%CSC%" /nologo /codepage:65001 /optimize /target:exe /out:"%TEMP%\PhotoSift-build.exe" /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll Server.cs || goto failed
move /y "%TEMP%\PhotoSift-build.exe" PhotoSift.exe >nul 2>nul || goto inuse
>PhotoSift.build echo(%SRC%
:run
"%~dp0PhotoSift-helper\PhotoSift.exe" %*
if errorlevel 1 pause
exit /b
:inuse
rem The old PhotoSift.exe is still running: start that one now; the new build is made next time.
del "%TEMP%\PhotoSift-build.exe" 2>nul
goto run
:failed
echo.
echo Could not build PhotoSift.exe.
if not exist PhotoSift.exe pause & exit /b 1
echo Starting the previous build instead.
goto run
