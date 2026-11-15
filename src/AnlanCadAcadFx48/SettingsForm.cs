// ⚙ 设置窗：baseUrl / apiKey / model / vlmModel 四字段 + 测试连接 + 保存并应用。
// 由 PaletteHost 的 ⚙ 按钮或 ANLANDEVSET 命令打开；保存即写盘（ActivePath）并经
// 回调重建引擎——免重启 acad。非模态：不锁 acad 与面板；幂等：已开则前置。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AnlanCadCore;

namespace AnlanCadAcadFx48
{
    public static class SettingsForm
    {
        private static Window _win;

        // apply 由 PaletteHost 提供：重建引擎；返回 false = 本轮 agent 还在跑，未应用
        public static void Show(LlmConfig cfg, Func<LlmConfig, bool> apply)
        {
            if (_win != null) { _win.Activate(); return; }

            var grid = new Grid();
            grid.Margin = new Thickness(12);
            for (int i = 0; i < 6; i++) grid.RowDefinitions.Add(new RowDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            TextBox urlBox = Field(grid, 0, "接口地址", cfg.BaseUrl);
            TextBox keyBox = Field(grid, 1, "API Key", cfg.ApiKey);
            TextBox modelBox = Field(grid, 2, "对话模型", cfg.Model);
            TextBox vlmBox = Field(grid, 3, "视觉模型", cfg.VlmModel);

            TextBlock status = new TextBlock();
            status.Text = "改完点「保存并应用」即时生效，无需重启 CAD。";
            status.TextWrapping = TextWrapping.Wrap;
            status.FontSize = 12;
            status.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x60, 0x68, 0x74));
            Grid.SetRow(status, 5);
            grid.Children.Add(status);

            StackPanel btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.HorizontalAlignment = HorizontalAlignment.Right;
            btns.Margin = new Thickness(0, 10, 0, 4);
            Grid.SetRow(btns, 4);
            grid.Children.Add(btns);

            Button test = new Button();
            test.Content = "测试连接";
            test.Width = 88;
            test.Height = 28;
            test.Margin = new Thickness(0, 0, 8, 0);
            btns.Children.Add(test);

            Button save = new Button();
            save.Content = "保存并应用";
            save.Width = 96;
            save.Height = 28;
            btns.Children.Add(save);

            test.Click += new RoutedEventHandler(delegate(object s, RoutedEventArgs e)
            {
                TestConnection(urlBox.Text.Trim(), keyBox.Text.Trim(), status);
            });

            save.Click += new RoutedEventHandler(delegate(object s, RoutedEventArgs e)
            {
                var c = new LlmConfig();
                c.BaseUrl = urlBox.Text.Trim();
                c.ApiKey = keyBox.Text.Trim();
                c.Model = modelBox.Text.Trim();
                c.VlmModel = vlmBox.Text.Trim();
                if (c.BaseUrl.Length == 0 || c.Model.Length == 0)
                {
                    status.Text = "接口地址和对话模型不能为空";
                    return;
                }
                try
                {
                    LlmConfig.Save(c, LlmConfig.ActivePath());
                }
                catch (Exception ex)
                {
                    status.Text = "写盘失败: " + ex.Message;
                    return;
                }
                if (apply(c))
                {
                    _win.Close();
                }
                else
                {
                    status.Text = "本轮还没跑完，等它结束再点保存（配置已写盘，重启后也会生效）";
                }
            });

            _win = new Window();
            _win.Title = "安岚助手 设置";
            _win.Width = 460;
            _win.SizeToContent = SizeToContent.Height;
            _win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _win.Topmost = true;   // acad 进程内开窗可能被主窗盖住，置顶保证老师看得到
            _win.ResizeMode = ResizeMode.NoResize;
            _win.Content = grid;
            _win.Closing += new System.ComponentModel.CancelEventHandler(
                delegate(object s, System.ComponentModel.CancelEventArgs e) { _win = null; });
            _win.Show();
        }

        private static TextBox Field(Grid g, int row, string label, string value)
        {
            var lb = new TextBlock();
            lb.Text = label;
            lb.VerticalAlignment = VerticalAlignment.Center;
            lb.FontSize = 13;
            Grid.SetRow(lb, row);
            Grid.SetColumn(lb, 0);
            g.Children.Add(lb);

            var tb = new TextBox();
            tb.Text = value ?? "";
            tb.Height = 28;
            tb.FontSize = 13;
            tb.VerticalContentAlignment = VerticalAlignment.Center;
            tb.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, 1);
            g.Children.Add(tb);
            return tb;
        }

        // 测试连接走后台线程（UI 不冻结），结果经 Dispatcher 回状态行
        private static void TestConnection(string baseUrl, string apiKey, TextBlock status)
        {
            if (baseUrl.Length == 0) { status.Text = "先填接口地址"; return; }
            status.Text = "测试中…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string msg;
                try
                {
                    string resp = HttpJson.Get(baseUrl.TrimEnd('/') + "/models", apiKey, 20000);
                    var root = new JsX().Deserialize<Dictionary<string, object>>(resp);
                    var ids = new List<string>();
                    object dataObj;
                    if (root != null && root.TryGetValue("data", out dataObj))
                    {
                        foreach (object o in HttpJson.ToArr(dataObj))
                        {
                            var m = o as Dictionary<string, object>;
                            object idObj;
                            if (m != null && m.TryGetValue("id", out idObj) && idObj != null)
                            {
                                ids.Add(idObj.ToString());
                                if (ids.Count >= 8) break;
                            }
                        }
                    }
                    msg = ids.Count > 0
                        ? "连接 OK，可用模型: " + string.Join(", ", ids.ToArray())
                        : "连通但未解析到模型列表: " + HttpJson.Trunc(resp, 120);
                }
                catch (Exception ex)
                {
                    msg = "失败: " + ex.Message;
                }
                status.Dispatcher.BeginInvoke(new Action(delegate { status.Text = msg; }));
            });
        }
    }
}
