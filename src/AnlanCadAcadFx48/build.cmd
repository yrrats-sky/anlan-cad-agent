@echo off
REM csc direct build: plugin dll (Core+Host) and smoke exe (Core+SmokeTest)
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set ACAD=D:\2023CAD\AutoCAD 2023
set Fx=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set WPF=%Fx%\WPF
set ROOT=%~dp0..\..
set WV2=%ROOT%\lib\WebView2
set OUT=%ROOT%\build\AnlanCadAcadFx48.dll
if not exist "%ROOT%\build" mkdir "%ROOT%\build"

set CORE=%ROOT%\src\Core\LlmConfig.cs %ROOT%\src\Core\ToolRegistry.cs %ROOT%\src\Core\AgentLoop.cs %ROOT%\src\Core\HttpJson.cs %ROOT%\src\Core\VlmClient.cs
set HOST=%ROOT%\src\AnlanCadAcadFx48\Entry.cs %ROOT%\src\AnlanCadAcadFx48\AgentEngine.cs %ROOT%\src\AnlanCadAcadFx48\PaletteHost.cs %ROOT%\src\AnlanCadAcadFx48\SettingsForm.cs %ROOT%\src\AnlanCadAcadFx48\WebShell.cs %ROOT%\src\AnlanCadAcadFx48\CadBridge.cs

"%CSC%" /nologo /target:library /optimize+ /out:"%OUT%" ^
  /r:"%ACAD%\acmgd.dll" ^
  /r:"%ACAD%\acdbmgd.dll" ^
  /r:"%ACAD%\accoremgd.dll" ^
  /r:"%WPF%\PresentationCore.dll" ^
  /r:"%WPF%\PresentationFramework.dll" ^
  /r:"%WPF%\WindowsBase.dll" ^
  /r:"%Fx%\System.dll" ^
  /r:"%Fx%\System.Core.dll" ^
  /r:"%Fx%\System.Drawing.dll" ^
  /r:"%Fx%\System.Xaml.dll" ^
  /r:"%Fx%\System.Web.Extensions.dll" ^
  /r:"%WV2%\Microsoft.Web.WebView2.Core.dll" ^
  /r:"%WV2%\Microsoft.Web.WebView2.Wpf.dll" ^
  %CORE% %HOST%
if errorlevel 1 (echo DLL BUILD FAILED & exit /b 1)
echo OK: %OUT%
REM WebView2 运行时三件 + wwwroot 随产物（acad 从 dll 同目录解析依赖）
copy /y "%WV2%\Microsoft.Web.WebView2.Core.dll" "%ROOT%\build\" >nul
copy /y "%WV2%\Microsoft.Web.WebView2.Wpf.dll" "%ROOT%\build\" >nul
copy /y "%WV2%\WebView2Loader.dll" "%ROOT%\build\" >nul
xcopy /e /i /y "%ROOT%\src\AnlanCadAcadFx48\wwwroot" "%ROOT%\build\wwwroot" >nul

"%CSC%" /nologo /target:exe /optimize+ /out:"%ROOT%\build\SmokeTest.exe" ^
  /r:"%Fx%\System.dll" ^
  /r:"%Fx%\System.Core.dll" ^
  /r:"%Fx%\System.Web.Extensions.dll" ^
  %CORE% "%ROOT%\src\SmokeTest\Program.cs"
if errorlevel 1 (echo EXE BUILD FAILED & exit /b 1)
echo OK: %ROOT%\build\SmokeTest.exe
