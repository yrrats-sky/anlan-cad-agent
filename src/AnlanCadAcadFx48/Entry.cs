// AnlanCadAgent 阶段1骨架：NETLOAD 加载入口 + 命令注册
// 目标：AutoCAD 2023 (R24.2) / .NET Framework 4.8 / x64
using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(AnlanCadAcadFx48.Commands))]

namespace AnlanCadAcadFx48
{
    public class Entry : IExtensionApplication
    {
        public const string Version = "v0.4.3";   // 唯一版本源（原 Entry/PaletteHost 三处硬编码且不一致）

        public void Initialize()
        {
            // NETLOAD 时的欢迎信息；CoreConsole 下可能无文档，包一层
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.Editor.WriteMessage(
                        "\n[安岚助手DEV] " + Version + " 已加载。命令: ANLANDEVPING / ANLANDEVPANEL / ANLANDEVASK / ANLANDEVSET / ANLANDEVWPF");
                }
            }
            catch (System.Exception)
            {
                // 无 UI 宿主时静默
            }
        }

        public void Terminate()
        {
        }
    }

    public class Commands
    {
        // 冒烟用：验证"命令注册 + 编辑器输出"通路（DEV 前缀避让主会话 AnLan.bundle）
        [CommandMethod("ANLANDEVPING")]
        public void Ping()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.Editor.WriteMessage("\nANLANDEV: pong! " + Entry.Version + " 命令通路 OK");
            }
        }

        // 弹出聊天面板（agent 闭环入口）：Web 壳为主 UI
        [CommandMethod("ANLANDEVPANEL")]
        public void Panel()
        {
            WebShell.Show();
        }

        // 自动化验证入口：ANLANDEVASK <一句话> 直接送进 agent 循环（免手点面板）
        [CommandMethod("ANLANDEVASK")]
        public void Ask()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            PromptStringOptions po = new PromptStringOptions("\n要发给助手的话: ");
            po.AllowSpaces = true;   // 英文带空格的指令整句接收（中文无空格不受影响）
            PromptResult pr = doc.Editor.GetString(po);
            if (pr.Status == PromptStatus.OK && pr.StringResult != null
                && pr.StringResult.Length > 0)
            {
                WebShell.AskFromCode(pr.StringResult);
            }
        }

        // 开图自动弹面板（acaddoc.lsp 的 S::STARTUP 调用）：会话内只自动弹一次，
        // 老师手动关掉面板后开新图不再重弹；想再开手动输 ANLANDEVPANEL
        [CommandMethod("ANLANDEVBOOT")]
        public void Boot()
        {
            WebShell.ShowOnce();
        }

        // 打开设置窗（⚙ 按钮同入口；保存即写盘+重建引擎，免重启 acad）
        [CommandMethod("ANLANDEVSET")]
        public void Set()
        {
            WebShell.OpenSettings();
        }

        // WebView2 Web 壳（与 ANLANDEVPANEL 同一入口，显式点名用）
        [CommandMethod("ANLANDEVWEB")]
        public void Web()
        {
            WebShell.Show();
        }

        // 旧 WPF 聊天面板兜底入口：Web 壳异常时（如 WebView2 运行时缺失）仍可用
        [CommandMethod("ANLANDEVWPF")]
        public void WpfPanel()
        {
            PaletteHost.Show();
        }
    }
}
