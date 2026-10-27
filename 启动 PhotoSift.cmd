@echo off
setlocal
rem PhotoSift: builds the helper once with the C# compiler that ships with Windows, then starts it.
rem To rebuild after editing PhotoSift-helper\Server.cs, delete PhotoSift-helper\PhotoSift.exe.
title PhotoSift
cd /d "%~dp0PhotoSift-helper"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist PhotoSift.exe (
  echo Preparing PhotoSift, first run only...
  "%CSC%" /nologo /codepage:65001 /optimize /target:exe /out:"%TEMP%\PhotoSift-build.exe" /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll Server.cs || goto failed
  move /y "%TEMP%\PhotoSift-build.exe" PhotoSift.exe >nul || goto failed
)
"%~dp0PhotoSift-helper\PhotoSift.exe" %*
if errorlevel 1 pause
exit /b
:failed
echo.
echo Could not build PhotoSift.exe.
pause
