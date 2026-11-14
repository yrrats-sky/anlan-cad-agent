// 共享 agent 引擎：配置加载/引擎构建/忙态/轮末 UNDO MARK 上收到这里，
// UI 宿主（Web 壳 WebShell、WPF 兜底面板 PaletteHost）只订阅事件做渲染。
// 线程契约：EnsureStarted/Ask/NewSession/ApplyConfig 由 CAD 主线程调（命令与面板
// 构建都在主线程）；Message 事件可能在 agent 工作线程触发（宿主自行 Dispatcher）；
// BusyChanged 在主线程触发（宿主可直接改 UI）。
using System;
using System.Windows.Threading;
using AnlanCadCore;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AnlanCadAcadFx48
{
    public static class AgentEngine
    {
        private static Dispatcher _ui;
        private static LlmConfig _cfg;
        private static ToolRegistry _tools;
        private static AgentLoop _agent;
        private static bool _busy;   // 一轮未完拒发新句：读写均在主线程（BeginInvoke 收敛），无需锁

        public static event Action<string, string> Message;   // (role, text)
        public static event Action<bool> BusyChanged;          // 忙态变化（宿主禁用/恢复输入）

        public static bool Busy { get { return _busy; } }
        public static LlmConfig Config { get { return _cfg; } }

        // 首个调用方持有 CAD 主线程 Dispatcher（命令/面板构建都在主线程，天然成立）
        public static void EnsureStarted()
        {
            if (_agent != null) return;
            _ui = Dispatcher.CurrentDispatcher;
            _cfg = LlmConfig.Load();
            RebuildAgent();
        }

        // 设置窗保存后即时生效：重建引擎（与 EnsureStarted 同构）。
        // 忙态中拒绝（防轮中途换引擎让旧循环写进新 registry）；配置已写盘，重启后也会生效。
        // 返回 false = 忙态未应用。主线程调用（SettingsForm 保存回调在主线程）
        public static bool ApplyConfig(LlmConfig cfg)
        {
            if (_busy) return false;
            _cfg = cfg;
            RebuildAgent();
            Fire("sys", "配置已应用：模型 " + _cfg.Model + " / VLM " + _cfg.VlmModel);
            return true;
        }

        private static void RebuildAgent()
        {
            _tools = new ToolRegistry();
            _agent = new AgentLoop(_cfg, _tools);
            _agent.OnMessage += OnLoopMessage;
            _agent.Completed += OnLoopCompleted;
            CadBridge.RegisterAll(_tools, _cfg, _ui);
        }

        // 统一发送入口（Web 壳 / WPF 输入行 / ANLANDEVASK 同一条路）。
        // "/new" 截获为开新会话；忙态与未配 Key 在主线程拦下并给 sys 提示
        public static void Ask(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            _ui.BeginInvoke(new Action(delegate
            {
                if (text == "/new") { NewSession(); return; }
                if (_busy) { Fire("sys", "上一轮还没跑完，稍等一下再发～"); return; }
                if (_cfg.ApiKey.Length == 0)
                {
                    Fire("sys", "未配置 apiKey：点 ⚙ 设置填入，或编辑 config.json。");
                    return;
                }
                _busy = true;
                FireBusy();
                _agent.Send(text);
            }));
        }

        public static void NewSession()
        {
            _ui.BeginInvoke(new Action(delegate
            {
                if (_busy) { Fire("sys", "上一轮还没跑完，结束后再 /new。"); return; }
                _agent.Reset();
                Fire("sys", "已开新会话，历史已清空");
            }));
        }

        // agent 工作线程 → 原样转发（宿主自行 marshal）
        private static void OnLoopMessage(string role, string text)
        {
            Fire(role, text);
        }

        // 一轮结束（成功或出错）：主线程恢复闲态 + 轮末打 UNDO MARK
        //（undo_back 一键回退到上一轮结束时的状态；原设计打在轮首会让"撤销上一轮"变空操作）
        private static void OnLoopCompleted()
        {
            _ui.BeginInvoke(new Action(delegate
            {
                _busy = false;
                FireBusy();
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                    // 裸选项字母 M（标记(M)/Mark 缩写同为 M）；_MARK 中文版不认会卡提示
                    doc.SendStringToExecute("_.UNDO M ", true, false, false);
            }));
        }

        private static void Fire(string role, string text)
        {
            var h = Message;
            if (h != null) h(role, text);
        }

        private static void FireBusy()
        {
            var h = BusyChanged;
            if (h != null) h(_busy);
        }
    }
}
