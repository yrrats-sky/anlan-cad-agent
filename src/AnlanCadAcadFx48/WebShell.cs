// Web 壳（转正版）：WebView2 承载 wwwroot\panel.html 全聊天 UI，经 JS↔C# 桥接
// AgentEngine（与 WPF 兜底面板同一条通路）。ANLANDEVPANEL/BOOT/SET/ASK 主入口已切到这里，
// 旧 WPF 面板保留为 ANLANDEVWPF 兜底；Web 壳自身初始化失败（无 WebView2 运行时/
// Loader 缺失）也会自动降级到 WPF 兼容面板（Fallback，会话内一次）。
// 桥协议（对象构建 JSON，守则：禁手写转义）：
//   JS→C#  {type:"ready"} 握手 / {type:"ask", text} 发言（"/new" 由引擎截获） / {type:"settings"} 开设置
//   C#→JS  {n:序号, type:"init",...} 首包 / {type:"msg", role, text, time} / {type:"busy", busy}
//   JS 侧按 n 去重（message/messagereceived 双事件名兜底防双显）
// 依赖：lib\WebView2\ 三个 dll 随 build.cmd 引用并复制到产物目录（acad 按需加载
// 从 bundle Contents 解析依赖，三者必须与主 dll 同目录）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;
using AnlanCadCore;
using Autodesk.AutoCAD.Windows;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AnlanCadAcadFx48
{
    public static class WebShell
    {
        private static PaletteSet _ps;
        private static WebView2 _wv;
        private static bool _autoShown;   // 会话内已自动弹过（ANLANDEVBOOT 用）
        private static bool _fallback;    // Web 壳不可用已降级，本会话不再重试
        private static int _seq;          // C#→JS 消息序号（JS 按号去重）
        // 初始化看门狗：实测 Loader 缺失时 CoreWebView2InitializationCompleted 事件
        // 不触发（控件黑屏静默失败），必须靠超时兜底转降级，不能只等事件
        private static System.Windows.Threading.DispatcherTimer _initWatch;

        public static void Show()
        {
            if (_fallback)
            {
                PaletteHost.Show();   // 已降级：所有入口永久走兼容面板
                return;
            }
            if (_ps == null)
            {
                // 预检不过直接降级，不创建 Web 壳——实测失败的 WebView2 环境创建会让
                // 同会话后续创建的 WPF 面板黑屏（渲染被污染），无运行时的机器必须
                // 避免走到创建那一步（分发决策5）
                if (!WebView2Available())
                {
                    Fallback("未检测到 WebView2 运行时，已直接使用兼容面板");
                    return;
                }
                // WebView2 类型级故障（运行时/Loader 缺失）在 EnsureShell 的 JIT/构造期抛出；
                // Show 本体不引用 WebView2 类型，异常在本层 try 捕获转降级
                try { EnsureShell(); }
                catch (Exception ex) { Fallback("Web 壳初始化失败，已切兼容面板（" + ex.Message + "）"); return; }
            }
            _ps.Visible = true;
        }

        // 运行时预检：Evergreen 看没看 EdgeUpdate Clients 的 pv（与 install.ps1 同键同判）；
        // Loader 是随主 dll 的 native 件，缺失/被改名时 Web 壳无法创建。两者齐才建壳
        private static bool WebView2Available()
        {
            string loader = Path.Combine(
                Path.GetDirectoryName(typeof(WebShell).Assembly.Location), "WebView2Loader.dll");
            if (!File.Exists(loader)) return false;
            string[] keys = new string[] {
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" };
            foreach (string k in keys)
            {
                try
                {
                    Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(k);
                    if (key != null)
                    {
                        using (key)
                        {
                            object pv = key.GetValue("pv");
                            if (pv != null && pv.ToString().Length > 0) return true;
                        }
                    }
                }
                catch (Exception) { }
            }
            try
            {
                Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
                if (key != null)
                {
                    using (key)
                    {
                        object pv = key.GetValue("pv");
                        if (pv != null && pv.ToString().Length > 0) return true;
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        private static void EnsureShell()
        {
            AgentEngine.EnsureStarted();
            AgentEngine.Message += OnEngineMessage;
            AgentEngine.BusyChanged += OnEngineBusy;

            _ps = new PaletteSet("安岚助手Web",
                new Guid("8D1F5B72-4C63-4A90-8E17-5F2D9C4B6A11"));   // 固定 GUID：记住停靠
            _ps.Style = PaletteSetStyles.ShowCloseButton
                      | PaletteSetStyles.ShowAutoHideButton;
            _ps.DockEnabled = DockSides.Left | DockSides.Right;
            _ps.MinimumSize = new System.Drawing.Size(340, 460);
            _ps.KeepFocus = true;   // 输入框不丢焦点；点画布即可回 CAD（WPF 面板同款）

            _wv = new WebView2();
            _wv.CreationProperties = new CoreWebView2CreationProperties();
            _wv.CreationProperties.UserDataFolder =
                Path.Combine(Path.GetTempPath(), "AnlanCadAgent_WebView2");
            // Source 预置：控件加载完成后自动初始化并导航（WPF 宿主标准路径）
            string html = Path.Combine(HtmlRoot(), "panel.html");
            if (File.Exists(html))
            {
                _wv.Source = new Uri(html);
            }
            _wv.CoreWebView2InitializationCompleted += OnInitDone;

            var host = new Grid();
            host.Children.Add(_wv);
            _ps.AddVisual("web", host);
            _ps.Dock = DockSides.Right;   // 必须在 AddVisual 之后设（PaletteHost 同款坑）

            // 看门狗：10s 内 CoreWebView2 还没建出来（事件也没报失败）→ 转降级。
            // 正常机器环境创建 1-3s；老机器冷启动留足余量
            _initWatch = new System.Windows.Threading.DispatcherTimer();
            _initWatch.Interval = TimeSpan.FromSeconds(10);
            _initWatch.Tick += delegate
            {
                _initWatch.Stop();
                if (!_fallback && _wv != null && _wv.CoreWebView2 == null)
                    Fallback("WebView2 初始化超时（运行时缺失或被阻止），已切兼容面板");
            };
            _initWatch.Start();
        }

        // 降级：藏掉（不拆）Web 面板 → 弹 WPF 兼容面板 → 面板内 sys 提示 + 留痕
        //（分发决策5：无 WebView2 运行时的机器功能不丢，会话内只降一次。
        // 只藏不拆：对停靠中的面板 Dispose 实测有让停靠容器渲染异常的风险）
        private static void Fallback(string reason)
        {
            if (_fallback) return;
            _fallback = true;
            StopInitWatch();
            var ps = _ps;
            _ps = null;
            _wv = null;
            if (ps != null)
            {
                try { ps.Visible = false; } catch (Exception) { }
            }
            PaletteHost.Show();
            PaletteHost.Announce(reason);
            // 降级留痕：现场排障 + 自动化验收证据（原因/两边面板状态）
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "anlan_fallback.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + reason
                    + " | webPalette=" + (ps != null ? "hidden" : "none")
                    + " | wpfPanel=" + (PaletteHost.IsPanelUp ? "up" : "down"));
            }
            catch (Exception) { }
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                    doc.Editor.WriteMessage("\n[安岚助手] " + reason);
            }
            catch (Exception) { }
        }

        // acaddoc.lsp 的 S::STARTUP 经 ANLANDEVBOOT 调这里：会话内只自动弹一次，
        // 老师手动关面板后开新图不再重弹；手动 ANLANDEVPANEL 不受影响
        public static void ShowOnce()
        {
            if (_autoShown) return;
            _autoShown = true;
            Show();
        }

        // 代码/命令行入口：与 UI 发送走同一条引擎通路（ANLANDEVASK 自动化验证用）
        public static void AskFromCode(string text)
        {
            Show();
            AgentEngine.Ask(text);
        }

        // ⚙ / ANLANDEVSET 入口：设置窗引擎无关，保存回调接引擎
        public static void OpenSettings()
        {
            Show();
            SettingsForm.Show(AgentEngine.Config, AgentEngine.ApplyConfig);
        }

        // wwwroot 与 dll 同目录（dev=build\wwwroot；部署=bundle Contents\wwwroot）
        private static string HtmlRoot()
        {
            return Path.Combine(
                Path.GetDirectoryName(typeof(WebShell).Assembly.Location), "wwwroot");
        }

        private static void OnInitDone(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            StopInitWatch();
            // 看门狗已先行降级（_wv 被清空）才迟到的事件：直接忽略防 NRE
            if (_fallback || _wv == null) return;
            // 运行时缺失（老 Win10/LTSC 未装 Evergreen）走这里：初始化失败而非异常抛出，
            // 同样转降级（分发决策5）
            if (!e.IsSuccess || _wv.CoreWebView2 == null)
            {
                string msg = (e.InitializationException != null)
                    ? e.InitializationException.Message : "初始化未成功";
                Fallback("WebView2 运行时不可用，已切兼容面板（" + msg + "）");
                return;
            }
            _wv.CoreWebView2.WebMessageReceived += OnWebMessage;
            _wv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        }

        private static void StopInitWatch()
        {
            if (_initWatch == null) return;
            _initWatch.Stop();
            _initWatch = null;
        }

        // JS→C#（WebMessageReceived 在 UI 线程触发）。ready 握手留痕文件：
        // 自动化验证的证据链 + 现场排障时判断桥是否连通
        private static void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string raw = "";
            try { raw = e.TryGetWebMessageAsString(); } catch (Exception) { }
            var d = Parse(raw);
            string type = GetStr(d, "type");
            if (type == "ready")
            {
                try
                {
                    File.WriteAllText(
                        Path.Combine(Path.GetTempPath(), "anlan_web_bridge.txt"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " bridge-up " + Entry.Version);
                }
                catch (Exception) { }
                SendInit();
            }
            else if (type == "ask")
            {
                AgentEngine.Ask(GetStr(d, "text"));
            }
            else if (type == "settings")
            {
                OpenSettings();
            }
        }

        // 首包：页面据此点亮状态条（版本/模型/Key 缺失告警/当前忙态）
        private static void SendInit()
        {
            LlmConfig cfg = AgentEngine.Config;
            var d = new Dictionary<string, object>();
            d["type"] = "init";
            d["version"] = Entry.Version;
            d["model"] = cfg.Model;
            d["vlm"] = cfg.VlmModel;
            d["hasKey"] = cfg.ApiKey.Length > 0;
            d["loadError"] = cfg.LoadError;
            d["busy"] = AgentEngine.Busy;
            Post(d);
        }

        // C#→JS：引擎事件可能在 agent 工作线程触发，经 WebView2 Dispatcher 回 UI 线程再投递。
        // 页面尚未 ready 时投递会被丢弃——可接受（init 会带上当前忙态兜底）
        private static void OnEngineMessage(string role, string text)
        {
            var wv = _wv;
            if (wv == null) return;
            wv.Dispatcher.BeginInvoke(new Action(delegate
            {
                var d = new Dictionary<string, object>();
                d["type"] = "msg";
                d["role"] = role;
                d["text"] = text;
                d["time"] = DateTime.Now.ToString("HH:mm:ss");
                Post(d);
            }));
        }

        private static void OnEngineBusy(bool busy)
        {
            var wv = _wv;
            if (wv == null) return;
            wv.Dispatcher.BeginInvoke(new Action(delegate
            {
                var d = new Dictionary<string, object>();
                d["type"] = "busy";
                d["busy"] = busy;
                Post(d);
            }));
        }

        // 投递：对象构建序列化 + 递增序号
        private static void Post(Dictionary<string, object> d)
        {
            var wv = _wv;
            if (wv == null || wv.CoreWebView2 == null) return;
            _seq++;
            d["n"] = _seq;
            wv.CoreWebView2.PostWebMessageAsJson(new JsX().Serialize(d));
        }

        private static Dictionary<string, object> Parse(string json)
        {
            if (json == null || json.Length == 0) return null;
            try
            {
                return new JsX().DeserializeObject(json)
                    as Dictionary<string, object>;
            }
            catch (Exception) { return null; }
        }

        private static string GetStr(Dictionary<string, object> d, string k)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v != null) return v.ToString();
            return "";
        }
    }
}
