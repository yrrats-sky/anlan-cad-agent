# anlan-cad-agent 项目守则

进程内 AutoCAD agent 插件（fork 分支，anlanai 教学助手 CAD 版）。**进度唯一真相源是 HANDOFF.md**，改完代码更新它。

## 硬约束（违反即编译不过/加载不过）
- 目标机 AutoCAD 2023（R24.2，D:\2023CAD\）+ netFx 4.8 + csc 直编零 NuGet → **只能 C# 5**：禁 `$""` 插值、`?.`、`nameof`、`out var`、表达式体成员；async/await 可用。
- 构建一律走 `src/AnlanCadAcadFx48/build.cmd`；新增 .cs 必须同步加进它的编译清单。
- **改码重编后必须重启 acad**（dll 会话内锁定）。

## 验证链路（每次改动后按序跑，缺一不可）
1. `build.cmd` 双产物零错
2. `build\SmokeTest.exe` → SCHEMA#0-10 全 OK + SMOKE OK
3. 重启 acad → ANLANDEVPING 回 pong
4. CAD 内实画：画线 / A4图框 / 截图自查三件套，对比 ModelSpace.Count

## 四大禁区（踩坑换来的修复，只可加注释，禁止简化/移除/重写）
1. HttpJson.cs 静态构造绕系统代理——Clash 节点挂时 .NET 全断，中转站直连可达
2. ToArr 数组形态防御（AgentLoop.ToArr；VlmClient 同款）——JavaScriptSerializer 数组非 object[] 形态会丢/NRE；**新写任何 LLM 响应解析必须走 ToArr 防御**
3. 系统变量一律 SendCommand+轮询，禁用 COM SetVariable（本机被拒 -2145320858）
4. HKCU 按需加载注册表方案——NETLOAD 本机静默卡死已弃用，别再走

## 线程与语义不变量
- 所有 CAD API 只能经 CadBridge `_ui.Invoke` 编组回主线程；工作线程不得直调。
- `_busy` 类重入标志放 AgentEngine（主线程 BeginInvoke 收敛读写），不放 AgentLoop（工作线程）。
- UNDO MARK 在轮末（AgentEngine.OnLoopCompleted）是有意设计，勿改回轮首。
- SendStringToExecute 用本机专属 4 参签名，提取/包装时原样保留。
- **UI 宿主（WebShell/PaletteHost）禁持有配置/引擎状态**——引擎上收 AgentEngine，宿主只订阅
  Message/BusyChanged 事件做渲染与转发；新 UI 宿主也走这条路。
- C#→JS 桥消息一律对象构建 JSON + 递增序号 n；JS 侧 'message'/'messagereceived' 双事件名
  兜底、按 n 去重防双显；ready 握手写痕 %TEMP%\anlan_web_bridge.txt（自动化验证证据）。

## 代码规范（防屎山条款）
- **命令流（SendStringToExecute）选项一律用裸字母**：`_.UNDO B`、`_.ZOOM E`。禁下划线前缀选项——`_BACK`/`_MARK`/`_E` 中文版命令行不认，UNDO 会挂在数值提示并吞掉后续全部命令流（11-10 实测，LASTPROMPT 滞留悬案的根因）。命令名部分（`_.UNDO`）保留 `_.` 不变。
- **截图类工具必须先置前自己进程的主窗口**再 CopyFromScreen（被遮挡时截到的是覆盖窗口，VLM 自查会跑偏烧轮数；置前自己进程窗口不受前台锁限制）。
- **PaletteSet 停靠记忆会坏**：异常会话（如看门狗降级建面板失败）会把 GUID 的停靠态写进 Profile.aws，之后每会话继承"对象活着但内容树为空"的黑屏；WPF 兜底面板因此用**新 GUID + 浮动（InitializeFloatingPosition）不 Dock**；同进程普通 WPF Window 渲染正常时可作最终退路。
- **WebView2 初始化可能静默失败**（Loader 缺失时 InitializationCompleted 不触发）：运行时预检（EdgeUpdate pv 三键 + Loader 文件存在）不过**不建壳直接降级**；已建壳则 10s 看门狗兜底转 Fallback；Fallback 只藏不拆（Dispose 有二次风险）并留痕 %TEMP%\anlan_fallback.txt。
- 工具执行失败：`JsonErr(name + ": " + ex.Message)` 带工具名；禁止吞异常。
- 禁止手写转义 JSON 单行字符串（schema/请求体），一律对象构建 + `ser.Serialize`。
- 参数解析失败必须抛异常，禁止静默回空字典让工具按缺省值"成功"执行。
- 拼 JSON 时控制符（<0x20）必须转义，别只处理 `\n \r \t \"`。
- 死代码直接删，不注释掉；不留无调用方的 public 方法（要保留就接线）。
- 版本号只用 Entry.Version 一处 const；超时等魔数收口 LlmConfig 常量。
- 命令名：ANLANDEVPING / ANLANDEVPANEL / ANLANDEVASK / ANLANDEVBOOT / ANLANDEVSET /
  ANLANDEVWEB / ANLANDEVWPF（主 UI=WebShell；WPF=兜底；旧脚本里的 ANLANPING 是错的）。
- 重复逻辑出现第三份之前必须提取共享（HTTP 解析、建层、Trunc 这类）。
- 限流嗅探只对"反序列化失败=非 JSON"的响应做子串匹配，合法 JSON 交 ParseChoiceMessage 判错。

## 历史与上下文
- `_messages` 无界增长会撞 429：BuildBody 前按轮边界裁剪（勿拆散 assistant.tool_calls 与对应 tool 消息对，否则 API 报错）。
- scripts/ 下 NETLOAD 流程已弃用；跑任何脚本前先看头部注释。
- LLM 响应解析三段式（error/choices/message）已有带防御的实现，新代码复用，不写第三份。

## 敏感
- build/config.json 有真 key、probe_key.txt 是探测 key——**绝不推送远端、不贴日志**。
- **dist/apikey.private.txt（若存在）与 anlan-cad-helper-push.zip 含烘焙 key**——绝不推送远端/上传/贴日志；key 文件用毕即删。
- 含中文的 .ps1 必须 UTF-8 带 BOM（PS5.1 无 BOM 按 GBK 读会解析失败）。
