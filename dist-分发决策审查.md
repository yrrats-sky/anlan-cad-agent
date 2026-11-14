# anlan-cad-agent 分发决策审查（2026-11-11，侧聊产出，未改代码）

> 背景：开发会话注意力全在"装进 CAD"，分发视角缺失——多项分发决策实为开发便利性的外溢，未真正拍板。
> 本审查按 **官网（anlanai.cc/downloads/）+ 地推** 双渠道重审整条分发链：
> 物料 → 教师机 → 安装 → 首配 key → 日常使用 → 更新 → 卸载/售后 → 归因计费。
> 基线：v0.4.0（Web 壳转正后）。范围：**只考虑教师个人机**（机房机全权限版已裁掉）。

## 决策逐条审查

### 决策 1：装载机制 = HKCU 注册键 + acaddoc.lsp 钩子自动弹面板

- 现状：本机端到端验证过（NETLOAD 卡死、ApplicationPlugins 目录扫描不认均已实测），HKCU 按需加载 + acaddoc.lsp 的 S::STARTUP 调 ANLANDEVBOOT，会话内只弹一次。
- 田野检验（3 个未审视假设）：
  1. **acaddoc.lsp 是全局共享钩子文件**——其他插件/学校脚本也写它。install.ps1 对"已有别家 S::STARTUP"只警告不处理（install.ps1:74-76），地推每撞一台人工一次。
  2. **"全程免管理员"承诺是假的**（install.ps1:5）——钩子写 `AcadLocation\Support`；本机 `D:\2023CAD\` 免管理员纯属非标安装福利，**标准安装在 C:\Program Files 下写 Support 必须管理员**，`$ErrorActionPreference='Stop'` 会死得难看。
  3. **绿色版/精简版 CAD 无 HKLM 注册表** → 安装器第一步定位失败即退出（install.ps1:32-34），且报错话术老师看不懂。
- **拍板（已定）**：钩子改写到 `%APPDATA%\Roaming\Autodesk\AutoCAD 2023\R24.2\<locale>\Support`——默认支持文件搜索路径内、天然 per-user、免管理员、卸载干净；注意 SFSP 里 install-dir 与 Roaming 同时存在 acaddoc.lsp 时"先找到者生效"，安装器需迁移清理旧位置。
- 绿色版明确检测 + "暂不支持"话术。acaddoc.lsp 方案本体保留（验证过的东西不为优雅翻掉）。

### 决策 2：安装器死锁 R24.2，非 2023 直接退出

- 现状：只枚举 `R24.2`（install.ps1:21-31），否则 exit 1。
- 田野检验：教师 CAD 版本 2020-2026 分布散；开发机只有 2023，**没有其他版本的 acdbmgd 引用程序集，想编矩阵也编不了**。
- **拍板（已定）**：安装器枚举全部 R 版本——R24.2 全量注册；R25/R26 注册并标记"待前向兼容验证"（R24 编译的托管 dll 进 2024+ 大概率成立，借一台测试机验一次即可）；R21-R23 明确提示不支持；**版本探测结果回传站点**（顺带解决装机可视化）。
- 试点期兜底：地推按"CAD 2023"筛选客群；装失败给友好提示+记录版本。

### 决策 3：无更新机制（换 dll = 逐台手工覆盖 bundle）

- 田野检验：铺开 20 台后最痛的不是安装是更新；v0.4.1 每出一个修复都要逐台覆盖+重启 acad。
- **拍板（已定，试点后第一批做）**：引擎 EnsureStarted 时查 version.json → 从 /downloads/ 拉新 bundle 替换，面板提示"重启 CAD 生效"。

### 决策 4：分发形态（**已按官网渠道前提修订**，2026-11-11 用户修正：主渠道 = anlanai.cc 官网下载）

原审查按"微信传文件夹"假设发现三断点；官网渠道前提下重排：

| 原断点 | 官网分发下的变化 |
|---|---|
| 双击 .ps1 = 记事本 | **不变**：install.bat 一行包装仍是硬需求 |
| 微信拦截 exe/脚本 | **消解**：主渠道改为官网下载，微信只传播 /downloads/ 链接不传文件 |
| 未签名撞 SmartScreen | **性质变化**：MOTW 只对 exe 触发 SmartScreen；bat 双击与 `powershell -ExecutionPolicy Bypass` 均不走 MOTW 检查 → **zip+bat 全链零 SmartScreen**；未签名单 exe 反而是自助转化杀手 |

**拍板（现行 = 2026-11-14 修订版；11-11 原拍板已废止，仅存档于本节末历史注）**：
1. 主分发形态 = **Inno Setup exe 安装包**（PrivilegesRequired=lowest 零 UAC、[Run] runhidden 调 install.ps1 保单一事实源、[Icons] 桌面 lnk、控制面板卸载）；
2. 托管 = **GitHub Releases 公开 repo**（密钥清点含 git 历史扫描前置）+ anlanai.cc /downloads/ 站内镜像双链接；
3. zip + install.bat **降级为备用卡片**，make-dist 继续产出不删；
4. OV 签名**不买**（触发条件 = 官网自助流失数据可测且难看，再回议题）；
5. downloads 页卡片 + html no-cache 不变（原 2/4 条），如实标注"当前支持 AutoCAD 2023"。

> **历史注（2026-11-11 原拍板，1/3/5 条已废止，仅存档）**：原定 zip+install.bat 为主内层形态、OV 签名提前到官网自助上线时、单 exe 推迟到有装机数据之后。推翻论证（2026-11-14 用户拍板）：地推渠道人在场兜底 SmartScreen 警告 + GitHub 域名信誉压低拦截 + exe 对教师四项实增益（少解压步/正规软件感/控制面板卸载/Inno 结构杀软误报更低）。「双击安装.bat」改名方案同时作废（exe 成主入口后备用轨不值得投入）。落地 spec 见 **dist-exe安装包落地方案.md**。

### 决策 5：WebView2 依赖零检测零兜底

- 现状：BOOT → WebShell.Show；无运行时机器（老 Win10/LTSC）上会炸或黑板，不会自动落 ANLANDEVWPF。
- **拍板（已定）**：WebShell.Show 捕获初始化失败 → 自动切 PaletteHost + sys 提示"已降级兼容面板"；安装器检测运行时（EdgeUpdate Clients 注册表 pv），静默装 Evergreen bootstrapper 留到后续。

### 决策 6：key 闭环（**被官网渠道顶到前置**）

- 现状：⚙ 手填 或 install.ps1 `-ApiKey` 预配；自助路径 4 步（注册→建 key→⚙→粘贴）对非技术用户必断；"面板内一键领 key"原后置。
- **拍板（已定）——双轨 zip**：
  - **地推轨**：私有 zip，打包时烘焙地推 key（`make-dist` 加 -ApiKey 注入 config），教师零配置，装机即归因；key 泄露风险可接受（站点可吊销）。
  - **官网轨**：公开 zip **不含 key**（公开渠道不能带凭据）→ 面板首开引导：跳注册页 → ⚙ 粘贴 key（最小闭环）；完整一键领 key 试点后做。
- **站点侧隐藏断点**：QuotaForNewUser=100000 ≈ **2667 token，连一轮工具循环都跑不完**——官网自助转化死在门口。官网渠道上线前提到"够跑 20-50 轮对话+工具调用"的量级。

### 决策 7：install.ps1 硬编码模型名 glm-5.3 / sensenova-6.8-flash

- 田野检验：站点渠道浮动（探测 4-6/7），装出去的几百台配置不跟站走。
- **拍板（已定）**：安装时从站点拉默认模型（/api/status 或专用小端点），失败才落本地硬编码兜底。

## 试点前必修清单（按验收口径列给开发会话）

1. **install.bat**（一行包装，`powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0install.ps1" %*` + pause）——验收：双击 bat 完成安装。
2. **钩子迁 Roaming Support** + 旧位置迁移清理——验收：C:\Program Files 安装的 CAD 免管理员装成。
3. **版本枚举 + 分级 + 友好提示** + 探测结果回传——验收：2024/2026 机器上安装不 exit 1，给出可读话术。
4. **WebShell catch 降级 WPF**——验收：禁用 WebView2 运行时（改注册表/改名 Loader）后 BOOT 出 WPF 面板。
5. **make-dist 双轨**：`-ApiKey` 注入（地推轨）/ 无 key（官网轨）——验收：两种 zip 各自产出。
6. **install.ps1 顺手清理**：空 if 死语句（install.ps1:101）、卸载器钩子段加结束 marker 精确删段。

## 站点侧配合项（anlanai.cc）

| 项 | 内容 | 时机 |
|---|---|---|
| /downloads/ 发布 | bundle zip + version.json（版本/sha256/支持版本）+ 三步图文卡 | 试点前 |
| no-cache | downloads html 去缓存 | 试点前 |
| QuotaForNewUser 提额 | 2667 token → 够 20-50 轮 | 官网渠道上线前 |
| install/heartbeat 端点 | 安装事件+版本回传（key 归因顺带闭环） | 试点中 |
| OV 签名 | 官网自助渠道上线时采购 | 上线前 |

## 不做清单（明确出局，防扩散）

- 机房机全权限版本（已裁）
- Win7 / 32 位（安装器检测即提示）
- 绿色版 CAD（无注册表，检测+话术）
- R21-R23 老版本（待前向兼容结论后再议）
- 中望/浩辰（挂起中，装载器走 LISP 桥是预留位）

## 状态标注

- **已拍板**：决策 1（Roaming 迁移）、2（枚举分级）、3（更新机制做不做/时机）、4（官网渠道+zip+bat+双轨，**11-14 修订为 Inno exe 主形态 + GitHub 托管，见决策 4 修订注**）、5（降级）、6（双轨 key）、7（拉站点默认）。
- 其中"具体实现细节"（如 version.json 字段、Roaming 迁移边界、枚举到的版本分级阈值）留开发会话落地时定。
- 本文档只审决策不动代码；整改清单 1-6 是开发会话下一批工作的验收基线。
