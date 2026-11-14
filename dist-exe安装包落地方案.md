# anlan-cad-agent exe 安装包落地方案（2026-11-14）

> 给执行会话的 spec。拍板背景：dist-分发决策审查.md 决策 4 已于 11-14 修订——
> 主分发形态 = **Inno Setup exe 安装包**，托管 = **GitHub Releases + 站内镜像双链接**，
> zip+install.bat 降级为备用卡片（继续产出，不删），OV 暂不买。
> 执行纪律沿用 CLAUDE.md 守则：拆项做、每项有验收口径、HANDOFF.md 记进度。

## 一、最终形态（用户视角）

双击 `anlan-cad-setup-vX.exe` → **全程零 UAC** → Inno 进度页 → 完成页 → 桌面出现「安岚CAD助手」lnk → 重启 CAD 开图右侧自动弹面板。

- 安装逻辑**单一事实源 = install.ps1**，Inno 只做壳（[Run] 调它）；禁止用 Inno [Registry] 重写安装逻辑
- 卸载 = 控制面板"应用"→ Inno 卸载器 → [UninstallRun] 调 uninstall.ps1 清钩子/HKCU/bundle

## 二、Inno 脚本要点（dist/setup.iss）

1. `[Setup]`：
   - `AppId` = 新申请固定 GUID（勿复用任何 PaletteSet GUID）
   - `PrivilegesRequired=lowest`（install.ps1 本就免管理员 → 全程零 UAC，exe 版核心增益）
   - `DefaultDirName={localappdata}\AnlanCadAgent`
   - `AppVersion` 由 ISCC `/D` 传入（源 = Entry.Version，版本单一源守则不破）
2. `[Files]`：`bundle\**` → `{app}\bundle`；install.ps1 / uninstall.ps1 → `{app}`
3. `[Run]`：
   ```
   Filename: powershell; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install.ps1""{code:ApiKeyParam}"; Flags: runhidden
   ```
   - `runhidden` 隐藏控制台（进度由 Inno 页面承担，防黑窗闪现吓人）
   - **双轨 key**：编译期 `ISCC /DAPIKEY=sk-xxx` 时经预处理 `#ifdef` 拼接 `-ApiKey` 参数（地推轨）；官网版不定义 → 装完 `hasKey=false`，面板首开引导 ⚙ 粘贴（决策 6 双轨延续）
4. `[Icons]`：`{userdesktop}` 建「安岚CAD助手」lnk（11-14 固定入口定调的实现件）
5. `[UninstallRun]`：`powershell ... uninstall.ps1`（`runhidden`）——Inno 默认先跑卸载脚本再删文件，ps1 删的是 bundle/HKCU/钩子，与 `{app}` 无重叠，顺序安全
6. 中文界面：`ChineseSimplified.isl` 在 Inno Setup 安装目录 `Languages\Unofficial\`，需随脚本携带
7. 编译机依赖：装 Inno Setup 6（记下 ISCC.exe 路径）

## 三、make-dist.cmd 增量

- 新增 exe 产物链：`ISCC setup.iss` → `dist\anlan-cad-setup-{ver}.exe`（版本 findstr 解析 Entry.cs 复用现有逻辑）
- version.json 扩展：`exeFile` / `exeSha256` 与 zip 字段并列（将来面板内检查更新读）
- 双轨保持：push 轨 = `ISCC /DAPIKEY`（烘焙 key 的 exe，**绝不外传**）；public 轨 = 无 key exe + 无 key zip

## 四、GitHub 托管准备（安全红线，先做）

- 公开仓库前置清点：`.gitignore` 堵死 `build/config.json`、`dist/apikey.private.txt`、`dist/*push*.zip`、probe_key 相关文件；**git 历史扫描确认无 key，有则重建仓库再公开**
- Release：tag 从 v0.4.2 起，assets = 公开 exe + zip + version.json
- README 三行版：下载 → 双击 exe → 重启 CAD（与 downloads 页图文同口径）

## 五、downloads 页（站内侧同步改）

- 主卡 = exe（GitHub Releases 链接），卡内镜像行 = 站内 zip（"下载慢/失败点这里"）
- no-cache 照决策 4.4 做（一直未做，本次随页面上线）
- 图文从"三步解压版"改为两步：下载 → 双击 → 重启 CAD

## 六、验收口径（拆项执行，每项独立验收）

1. **Inno 壳**：双击 exe 全链零 UAC、零控制台黑窗闪现（runhidden 生效）、装完桌面 lnk 在 → 重启 CAD 面板自动弹（`%TEMP%\anlan_web_bridge.txt` 出现 bridge-up）
2. **卸载**：控制面板卸载 → 钩子/HKCU/bundle 清净（uninstall.ps1 现有断言复用）→ Inno 自身 `{app}` 清净 → 重装可达
3. **双轨**：`/DAPIKEY` exe 装机 key 注入 ✓；public exe 装机 `hasKey=false` 且面板提示可见
4. **GitHub**：公开 repo 无密钥（含历史扫描）→ 匿名下载 assets 可用
5. **回归**：zip 轨仍可装（make-dist 改造未破坏既有产物）

## 七、明确不做（防扩散）

- OV 签名（触发条件 = 官网自助流失数据可测且难看，再回议题）
- MSIX / ClickOnce
- 重型常驻启动器（11-14 定调不变：Inno 是一次性安装动作，非启动器）
- 「双击安装.bat」改名（zip 已降级备用轨，不值得投入）
- 机房机 / Win7 / 32 位 / 绿色版 CAD（维持不做清单）

## 八、风险与观察项

| 风险 | 处置 |
|---|---|
| SmartScreen：GitHub 域名信誉能降档但不保证归零 | 自助上线后观察弃装率；难看再买 OV |
| 杀软误报 | Inno 结构误报率低于 ps1 链；真被报走 360 申诉流程 |
| GitHub 大陆可达性波动 | 双链接设计兜底（站内镜像） |
| 卸载时 acad 占用 bundle dll | uninstall.ps1 已有"先关 CAD"话术；Inno 卸载页复用同款提示 |
