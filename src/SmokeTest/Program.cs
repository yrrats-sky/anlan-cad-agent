// 阶段2 冒烟：不碰 CAD。验证 anlanai 端点连通 + key 有效 + 模型支持 function calling + 循环逻辑
// 用法：SmokeTest.exe [测试指令]   缺省指令：画一条从(0,0)到(100,50)的线
using System;
using System.Collections.Generic;
using System.Threading;
using AnlanCadCore;

namespace AnlanCadSmoke
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            HttpJson.EnsureTls();   // models 等直连调用也要 TLS1.2
            LlmConfig cfg = LlmConfig.Load();
            Console.WriteLine("config: " + (LlmConfig.FindPath() ?? "(缺省，未找到文件)"));
            Console.WriteLine("baseUrl: " + cfg.BaseUrl + "  model: " + cfg.Model);
            Console.WriteLine("apiKey: " + (string.IsNullOrEmpty(cfg.ApiKey)
                ? "(空——填好再跑)" : "(已配置，不回显)"));
            if (string.IsNullOrEmpty(cfg.ApiKey)) return 2;

            // schema 自检：8 个工具 JSON 必须全部可解析
            var ser0 = new AnlanCadCore.JsX();
            for (int i = 0; i < ToolRegistry.Schemas.Length; i++)
            {
                try
                {
                    ser0.DeserializeObject(ToolRegistry.Schemas[i]);
                    Console.WriteLine("SCHEMA#" + i + " OK");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("SCHEMA#" + i + " 坏: " + HttpJson.Trunc(ex.Message, 120));
                    Console.WriteLine("  原文: " + HttpJson.Trunc(ToolRegistry.Schemas[i], 160));
                }
            }

            try
            {
                Console.WriteLine("\n== /v1/models 可用模型 ==");
                Console.WriteLine(HttpJson.Trunc(HttpGetModels(cfg), 900));
            }
            catch (Exception ex)
            {
                Console.WriteLine("models 拉取失败: " + ex.Message);
            }

            var tools = new ToolRegistry();
            foreach (string s in new string[] {
                "draw_line", "draw_circle", "draw_text", "add_dim_aligned",
                "list_layers", "create_layer", "set_current_layer", "zoom_extents",
                "undo_back", "draw_frame_a4", "screenshot_and_check" })
            {
                string name = s;
                tools.Register(name, delegate(Dictionary<string, object> a)
                {
                    return "{\"ok\":true,\"mock\":true,\"args\":" + JsonArgs(a) + "}";
                });
            }

            var agent = new AgentLoop(cfg, tools);
            var done = new ManualResetEvent(false);
            bool sawAssistant = false;
            agent.OnMessage += delegate(string role, string text)
            {
                if (role == "assistant") sawAssistant = true;
                Console.WriteLine("[" + role + "] " + HttpJson.Trunc(text, 400));
            };
            agent.Completed += delegate { done.Set(); };

            string ask = args.Length > 0
                ? string.Join(" ", args)
                : "画一条从(0,0)到(100,50)的线，画完告诉我画了什么";
            Console.WriteLine("\n== 测试指令: " + ask + " ==\n");
            agent.Send(ask);

            if (!done.WaitOne(LlmConfig.ChatTimeoutMs))
            {
                Console.WriteLine("[超时] " + (LlmConfig.ChatTimeoutMs / 1000) + "s 无完成信号");
                return 3;
            }
            if (!sawAssistant)
            {
                Console.WriteLine("\nSMOKE FAILED（未收到 assistant 回复，看上面 error 行）");
                return 1;
            }
            Console.WriteLine("\nSMOKE OK");
            return 0;
        }

        private static string HttpGetModels(LlmConfig cfg)
        {
            return HttpJson.Get(cfg.BaseUrl.TrimEnd('/') + "/models",
                cfg.ApiKey, 30000);
        }

        private static string JsonArgs(Dictionary<string, object> a)
        {
            var parts = new List<string>();
            foreach (var kv in a)
            {
                parts.Add(ToolRegistry.Quote(kv.Key) + ":" + ToolRegistry.Quote(
                    kv.Value == null ? "" : kv.Value.ToString()));
            }
            return "{" + string.Join(",", parts.ToArray()) + "}";
        }
    }
}
