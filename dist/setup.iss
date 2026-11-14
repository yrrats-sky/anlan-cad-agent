; 安岚CAD助手 Inno 安装壳（dist-exe安装包落地方案.md，2026-11-14）
; 安装逻辑单一事实源 = install.ps1，Inno 只做壳（[Run] 调用）；禁止用 Inno [Registry] 重写安装逻辑
; 编译（make-dist.cmd 自动调用）：
;   ISCC /DAPPVER=0.4.2 /DRELVER=v0.4.2 /DEXEOUT=anlan-cad-setup-v0.4.2 setup.iss        ← 官网轨
;   ISCC /DAPPVER=0.4.2 /DRELVER=v0.4.2 /DEXEOUT=...-push /DAPIKEY=sk-xxx setup.iss      ← 地推轨（产物绝不外传）
; 随脚本携带：ChineseSimplified.isl（LanguageCodePage=936，来自 issrc is-6_7_4 Unofficial）
#ifndef APPVER
  #error APPVER 未定义（ISCC /DAPPVER=...）
#endif
#ifndef RELVER
  #error RELVER 未定义（ISCC /DRELVER=...）
#endif
#ifndef EXEOUT
  #define EXEOUT "anlan-cad-setup-" + RELVER
#endif
#ifdef APIKEY
  #define APIKEYARG " -ApiKey " + APIKEY
#else
  #define APIKEYARG ""
#endif

[Setup]
AppId={{E078A869-58DC-4C15-BB39-DF3E4BD63812}
AppName=安岚CAD助手
AppVersion={#APPVER}
AppPublisherURL=https://anlanai.cc
DefaultDirName={localappdata}\AnlanCadAgent
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
; 无 UninstallableIconFile 之类花活：卸载入口=控制面板"应用"
UninstallDisplayName=安岚CAD助手
OutputDir=.
OutputBaseFilename={#EXEOUT}
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Files]
Source: "bundle\*"; DestDir: "{app}\bundle"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "install.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "uninstall.ps1"; DestDir: "{app}"; Flags: ignoreversion

; 换轨防串 key（11-14 实测第二形态）：Inno [Files] 只覆盖不删除——push 装过再装 public，
; {app} 里残留的烘焙 config.json 会被 install.ps1 当注入源串 key。安装前先删，
; 随后 [Files] 带来的（push 有/public 无）决定最终状态；install.ps1 内还有一道双向同步双保险
[InstallDelete]
Type: files; Name: "{app}\bundle\Contents\config.json"

[Run]
; runhidden 防黑窗闪现（进度由 Inno 页面承担）；失败时 [Code] 的组件校验兜底提示
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install.ps1""{#APIKEYARG}"; Flags: runhidden; StatusMsg: "正在安装组件（免管理员，全程无 UAC）…"

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall.ps1"""; Flags: runhidden; RunOnceId: "AnlanUninstallPs1"

; {app} 为本安装器专属目录：卸载后兜底清整——多次覆盖安装会留下递增编号的
; 旧卸载器（unins00N.dat/exe），Inno 只自删当前那份，残留靠这里清（11-14 实测）
[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
// 桌面「安岚CAD助手」lnk（11-14 固定入口定调的实现件）：目标=第一台实测存在的 acad.exe，
// install.ps1 同款 HKLM 枚举；CAD 未装时不建（老师装好 CAD 重跑安装包即可补建）
procedure CreateDesktopLnk;
var
  Versions, Locales: TArrayOfString;
  I, J: Integer;
  Base, Loc, AcadExe: String;
begin
  Base := 'SOFTWARE\Autodesk\AutoCAD';  // HKLM64：Inno 默认 32 位模式会把 HKLM 读重定向 Wow6432Node，acad 键在 64 位视图
  if not RegGetSubkeyNames(HKLM64, Base, Versions) then Exit;
  for I := 0 to GetArrayLength(Versions) - 1 do
  begin
    if not RegGetSubkeyNames(HKLM64, Base + '\' + Versions[I], Locales) then Continue;
    for J := 0 to GetArrayLength(Locales) - 1 do
    begin
      if RegQueryStringValue(HKLM64, Base + '\' + Versions[I] + '\' + Locales[J], 'AcadLocation', Loc) then
      begin
        AcadExe := AddBackslash(Loc) + 'acad.exe';
        if FileExists(AcadExe) then
        begin
          try
            CreateShellLink(ExpandConstant('{userdesktop}\安岚CAD助手.lnk'),
              '安岚CAD助手（打开 CAD 即自动弹出助手面板）',
              AcadExe, '', ExtractFileDir(AcadExe), AcadExe, 0, SW_SHOWNORMAL);
          except
          end;
          Exit;
        end;
      end;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // install.ps1 runhidden 跑在后台：以组件落位为其成功的代理证据，失败给老师一句人话
    if not FileExists(ExpandConstant('{app}\bundle\Contents\AnlanCadAcadFx48.dll')) then
      SuppressibleMsgBox('安装组件未就位，安装可能未成功。请重新运行安装包；若仍失败，请改用官网 zip 安装包。', mbError, MB_OK, IDOK);
    CreateDesktopLnk;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  // 与 uninstall.ps1 头部话术同口径（bundle dll 被 acad 占用会残留）
  Result := SuppressibleMsgBox('建议先关闭 AutoCAD 再继续卸载，否则部分文件可能因占用而残留。' #13#10 #13#10 '是否继续卸载？', mbConfirmation, MB_OKCANCEL, IDOK) = IDOK;
end;
