@echo off
REM Assemble dist bundle + dual-track zips + dual-track exe installers (Inno).
REM ASCII+CRLF required.
REM Usage: make-dist.cmd [ApiKey]
REM   push (di-tui) track: pass ApiKey as arg, or put it in dist\apikey.private.txt
REM     (file, first line = key; NEVER commit/upload that file or the push artifacts)
REM   public (official-site) track: zip/exe without any key/config.json inside
REM   exe build requires Inno Setup 6 (ISCC.exe); skipped gracefully if absent
setlocal
set BUNDLE=%~dp0bundle
set PRIVATEKEY=
if not "%~1"=="" set PRIVATEKEY=%~1
if "%PRIVATEKEY%"=="" if exist "%~dp0apikey.private.txt" set /p PRIVATEKEY=<"%~dp0apikey.private.txt"

REM 版本单一源 = Entry.cs（findstr 解析；exe 文件名与 version.json 共用）
set VER=
for /f "tokens=2 delims==" %%a in ('findstr /C:"public const string Version" "%~dp0..\src\AnlanCadAcadFx48\Entry.cs"') do (
  for /f "tokens=1 delims=;" %%b in ("%%a") do set VER=%%b
)
set VER=%VER: =%
set VER=%VER:"=%
if "%VER%"=="" (echo version parse FAILED & exit /b 1)
set VER2=%VER:v=%

if not exist "%BUNDLE%\Contents" mkdir "%BUNDLE%\Contents"
copy /y "%~dp0..\build\AnlanCadAcadFx48.dll" "%BUNDLE%\Contents\" >nul
if errorlevel 1 (echo dll copy FAILED - close AutoCAD first & exit /b 1)
copy /y "%~dp0..\lib\WebView2\Microsoft.Web.WebView2.Core.dll" "%BUNDLE%\Contents\" >nul
copy /y "%~dp0..\lib\WebView2\Microsoft.Web.WebView2.Wpf.dll" "%BUNDLE%\Contents\" >nul
copy /y "%~dp0..\lib\WebView2\WebView2Loader.dll" "%BUNDLE%\Contents\" >nul
xcopy /e /i /y "%~dp0..\build\wwwroot" "%BUNDLE%\Contents\wwwroot" >nul
copy /y "%~dp0PackageContents.template.xml" "%BUNDLE%\PackageContents.xml" >nul
echo dist bundle ready: %BUNDLE%

REM ---- stage deploy payload (what teachers get in the zip) ----
set PAYLOAD=%~dp0payload
if exist "%PAYLOAD%" rmdir /s /q "%PAYLOAD%"
mkdir "%PAYLOAD%"
copy /y "%~dp0install.bat" "%PAYLOAD%\" >nul
copy /y "%~dp0install.ps1" "%PAYLOAD%\" >nul
copy /y "%~dp0uninstall.ps1" "%PAYLOAD%\" >nul
xcopy /e /i /y "%BUNDLE%" "%PAYLOAD%\bundle\" >nul

REM ---- push track zip: bake key into bundle\Contents\config.json ----
if not "%PRIVATEKEY%"=="" (
  > "%BUNDLE%\Contents\config.json" echo {
  >>"%BUNDLE%\Contents\config.json" echo   "baseUrl": "https://anlanai.cc/v1",
  >>"%BUNDLE%\Contents\config.json" echo   "apiKey": "%PRIVATEKEY%",
  >>"%BUNDLE%\Contents\config.json" echo   "model": "glm-5.3",
  >>"%BUNDLE%\Contents\config.json" echo   "vlmModel": "sensenova-6.8-flash"
  >>"%BUNDLE%\Contents\config.json" echo }
  xcopy /e /i /y "%BUNDLE%" "%PAYLOAD%\bundle\" >nul
  powershell -NoProfile -Command "Compress-Archive -Path '%PAYLOAD%\*' -DestinationPath '%~dp0anlan-cad-helper-push.zip' -Force"
  del "%BUNDLE%\Contents\config.json" 2>nul
  del "%PAYLOAD%\bundle\Contents\config.json" 2>nul
  echo push zip ready: anlan-cad-helper-push.zip  (contains baked key - keep private)
)

REM ---- public track zip: no key anywhere ----
powershell -NoProfile -Command "Compress-Archive -Path '%PAYLOAD%\*' -DestinationPath '%~dp0anlan-cad-helper.zip' -Force"
echo public zip ready: anlan-cad-helper.zip  (upload to anlanai.cc/downloads/)

REM ---- exe installers (Inno 壳，安装逻辑单一事实源=install.ps1；spec=dist-exe安装包落地方案.md) ----
REM 顺序敏感：public exe 必须在 bundle 无烘焙 config 时编（防 key 进公开产物）；
REM push exe 编译前重新烘焙（zip 段末尾已删 config——11-14 实测漏烘焙根因），编译后即删
set EXEBUILT=
set ISCC=
if defined INNOHOME if exist "%INNOHOME%\ISCC.exe" set ISCC=%INNOHOME%\ISCC.exe
if "%ISCC%"=="" if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if "%ISCC%"=="" if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if "%ISCC%"=="" if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe
if "%ISCC%"=="" (
  echo [warn] ISCC.exe not found - exe installers skipped, zip chain unaffected
  echo [warn] install Inno Setup 6, or set INNOHOME to its folder, and re-run
  goto after-exe
)
"%ISCC%" /DAPPVER="%VER2%" /DRELVER="%VER%" /DEXEOUT="anlan-cad-setup-%VER%" "%~dp0setup.iss"
if errorlevel 1 (echo ISCC public build FAILED & exit /b 1)
echo public exe ready: anlan-cad-setup-%VER%.exe  (upload to GitHub Releases + downloads link)
if not "%PRIVATEKEY%"=="" (
  > "%BUNDLE%\Contents\config.json" echo {
  >>"%BUNDLE%\Contents\config.json" echo   "baseUrl": "https://anlanai.cc/v1",
  >>"%BUNDLE%\Contents\config.json" echo   "apiKey": "%PRIVATEKEY%",
  >>"%BUNDLE%\Contents\config.json" echo   "model": "glm-5.3",
  >>"%BUNDLE%\Contents\config.json" echo   "vlmModel": "sensenova-6.8-flash"
  >>"%BUNDLE%\Contents\config.json" echo }
  "%ISCC%" /DAPPVER="%VER2%" /DRELVER="%VER%" /DEXEOUT="anlan-cad-setup-%VER%-push" /DAPIKEY="%PRIVATEKEY%" "%~dp0setup.iss"
  del "%BUNDLE%\Contents\config.json" 2>nul
  if errorlevel 1 (echo ISCC push build FAILED & exit /b 1)
  echo push exe ready: anlan-cad-setup-%VER%-push.exe  (contains baked key - keep private)
  set EXEBUILT=1
)
if "%PRIVATEKEY%"=="" set EXEBUILT=1

:after-exe
REM ---- version.json: upload beside the zip on /downloads/ (contract: version/sha256/supported + exe) ----
REM 版本号单一源 = Entry.cs（上方解析）；supported 与 install.ps1 分级一致，pending 版不入列
set SHA256=
set EXESHA=
set EXEFILE=anlan-cad-setup-%VER%.exe
for /f %%h in ('powershell -NoProfile -Command "(Get-FileHash -Algorithm SHA256 -LiteralPath '%~dp0anlan-cad-helper.zip').Hash"') do set SHA256=%%h
if defined EXEBUILT for /f %%h in ('powershell -NoProfile -Command "(Get-FileHash -Algorithm SHA256 -LiteralPath '%~dp0%EXEFILE%').Hash"') do set EXESHA=%%h
> "%~dp0version.json" echo {
>> "%~dp0version.json" echo   "version": "%VER%",
>> "%~dp0version.json" echo   "file": "anlan-cad-helper.zip",
>> "%~dp0version.json" echo   "sha256": "%SHA256%",
>> "%~dp0version.json" echo   "exeFile": "%EXEFILE%",
>> "%~dp0version.json" echo   "exeSha256": "%EXESHA%",
>> "%~dp0version.json" echo   "supported": [ "R24.2" ],
>> "%~dp0version.json" echo   "notes": "AutoCAD 2023 full; 2021-2026 pending forward-compat check"
>> "%~dp0version.json" echo }
echo version.json ready: %VER% sha256=%SHA256% exeSha256=%EXESHA%

rmdir /s /q "%PAYLOAD%"
endlocal
