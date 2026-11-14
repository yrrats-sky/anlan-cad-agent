// 旧版 WPF 聊天面板：Web 壳转正后降级为兜底入口（ANLANDEVWPF）。
// 配置/引擎/忙态已上收 AgentEngine，这里只剩 RichTextBox 渲染与转发，
// 消息回调从 agent 工作线程经 Dispatcher 回 UI
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.AutoCAD.Windows;

namespace AnlanCadAcadFx48
{
    public static class PaletteHost
    {
        private static PaletteSet _ps;
        private static RichTextBox _chat;
        private static TextBox _input;
        private static TextBlock _status;

        public static void Show()
        {
            if (_ps == null)
            {
                AgentEngine.EnsureStarted();
                AgentEngine.Message += OnEngineMessage;
                AgentEngine.BusyChanged += OnEngineBusy;

                _ps = new PaletteSet("安岚助手DEV",
                    new Guid("4B7E2C91-8A35-4F6D-9E27-C15D08A3B7F4"));   // 新 GUID：旧 GUID 的停靠记忆被异常会话写坏（黑屏悬案），换新甩历史
                _ps.Style = PaletteSetStyles.ShowCloseButton
                          | PaletteSetStyles.ShowAutoHideButton
                          | PaletteSetStyles.ShowPropertiesMenu;
                _ps.DockEnabled = DockSides.Left | DockSides.Right;   // 只允许左右停靠，可自行换边
                _ps.MinimumSize = new System.Drawing.Size(320, 420);
                _ps.KeepFocus = true;   // 输入框不丢焦点；点画布即可回 CAD
                _ps.AddVisual("chat", BuildUi());
                // 浮动定位靠右（工作区右缘），别盖左上 ribbon；坐标系为屏幕坐标
                double w = 340, h = 500;
                var wa = System.Windows.SystemParameters.WorkArea;
                _ps.InitializeFloatingPosition(
                    new Rect(wa.Right - w - 10, wa.Top + 60, w, h));
                var cfg = AgentEngine.Config;
                string cfgMsg = cfg.ApiKey.Length == 0
                    ? "｜⚠ 未配置 apiKey（config.json）"
                    : "｜模型: " + cfg.Model + " / VLM: " + cfg.VlmModel;
                if (cfg.LoadError.Length > 0) cfgMsg += "｜⚠ " + cfg.LoadError;
                AppendMsg("sys", "安岚助手DEV " + Entry.Version + "（WPF 兜底；Web 壳请输 ANLANDEVWEB）" + cfgMsg);
                // 不设 Dock：浮动规避停靠状态损坏（11-11 黑屏悬案查清根因前保持浮动）
            }
            _ps.Visible = true;
        }

        // ⚙ 按钮 / 兜底设置入口：设置窗引擎无关，保存回调接引擎
        public static void OpenSettings()
        {
            AgentEngine.EnsureStarted();
            SettingsForm.Show(AgentEngine.Config, AgentEngine.ApplyConfig);
        }

        // 系统级提示直写对话流（供 WebShell 降级等跨宿主事件使用；工作线程安全）
        public static void Announce(string text)
        {
            var ui = _chat != null ? _chat.Dispatcher : null;
            if (ui == null) return;
            ui.BeginInvoke(new Action(delegate
            {
                AppendMsg("sys", text);
            }));
        }

        // 面板对象是否已建且可见（WebShell 降级留痕/排障用）
        public static bool IsPanelUp
        {
            get { return _ps != null && _ps.Visible; }
        }

        private static UIElement BuildUi()
        {
            DockPanel root = new DockPanel();
            root.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1F, 0x2B));

            // 底部：输入行（⚙ + 输入框 + 发送）与状态
            DockPanel inputRow = new DockPanel();
            inputRow.Margin = new Thickness(8);
            Button send = new Button();
            send.Content = "发送";
            send.Width = 56;
            send.Height = 28;
            send.Margin = new Thickness(6, 0, 0, 0);
            send.Click += new RoutedEventHandler(OnSendClick);
            DockPanel.SetDock(send, Dock.Right);

            Button gear = new Button();
            gear.Content = "⚙";
            gear.Width = 34;
            gear.Height = 28;
            gear.Margin = new Thickness(0, 0, 6, 0);
            gear.ToolTip = "设置（接口/Key/模型），保存即生效";
            gear.Click += new RoutedEventHandler(OnGearClick);
            DockPanel.SetDock(gear, Dock.Left);

            _input = new TextBox();
            _input.Height = 28;
            _input.FontSize = 13;
            _input.VerticalContentAlignment = VerticalAlignment.Center;
            _input.KeyDown += new KeyEventHandler(OnInputKeyDown);
            inputRow.Children.Add(send);
            inputRow.Children.Add(gear);
            inputRow.Children.Add(_input);
            DockPanel.SetDock(inputRow, Dock.Bottom);

            _status = new TextBlock();
            _status.Text = "就绪";
            _status.FontSize = 11;
            _status.Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA6));
            _status.Margin = new Thickness(8, 0, 8, 6);
            DockPanel.SetDock(_status, Dock.Bottom);

            // 中部：对话流
            _chat = new RichTextBox();
            _chat.IsReadOnly = true;
            _chat.IsReadOnlyCaretVisible = false;
            _chat.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x26, 0x2F));
            _chat.Foreground = Brushes.White;
            _chat.BorderThickness = new Thickness(0);
            _chat.FontSize = 13;
            _chat.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var doc = new FlowDocument();
            doc.PageWidth = 320;   // 面板窄，自动换行
            _chat.Document = doc;

            root.Children.Add(inputRow);
            root.Children.Add(_status);
            root.Children.Add(_chat);
            return root;
        }

        private static void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendFromUi();
                e.Handled = true;
            }
        }

        private static void OnSendClick(object sender, RoutedEventArgs e)
        {
            SendFromUi();
        }

        private static void OnGearClick(object sender, RoutedEventArgs e)
        {
            OpenSettings();
        }

        private static void SendFromUi()
        {
            string text = (_input.Text ?? "").Trim();
            if (text.Length == 0) return;
            _input.Clear();
            AgentEngine.Ask(text);   // "/new" 由引擎截获为开新会话
        }

        private static void OnEngineMessage(string role, string text)
        {
            // agent 工作线程 → UI 线程
            var ui = _chat != null ? _chat.Dispatcher : null;
            if (ui == null) return;
            ui.BeginInvoke(new Action(delegate
            {
                AppendMsg(role, text);
                if (role == "tool") _status.Text = "执行工具…";
            }));
        }

        private static void OnEngineBusy(bool busy)
        {
            var ui = _chat != null ? _chat.Dispatcher : null;
            if (ui == null) return;
            ui.BeginInvoke(new Action(delegate
            {
                _status.Text = busy ? "思考中…" : "就绪";
            }));
        }

        private static void AppendMsg(string role, string text)
        {
            if (_chat == null) return;
            FlowDocument doc = _chat.Document;
            Paragraph p = new Paragraph(new Run(text));
            p.Margin = new Thickness(0, 4, 0, 4);
            if (role == "user") p.Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0x7E));
            else if (role == "assistant") p.Foreground = Brushes.White;
            else if (role == "error") p.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x5A));
            else p.Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA6));   // sys/tool
            doc.Blocks.Add(p);
            _chat.ScrollToEnd();
        }
    }
}
