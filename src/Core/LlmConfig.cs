// LLM 配置：OpenAI 兼容端点（anlanai 中转站）。key 只存本地 config.json，不回显不外传。
// 查找顺序：环境变量 ANLAN_CAD_AGENT_CONFIG → %APPDATA%\AnlanCadAgent\config.json → dll 同目录
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;

namespace AnlanCadCore
{
    public class LlmConfig
    {
        public string BaseUrl = "https://anlanai.cc/v1";
        public string ApiKey = "";
        public string Model = "deepseek-chat";
        public string VlmModel = "glm-4.6v";   // 截图回环校验用的视觉模型

        // 超时收口（原散落 AgentLoop/VlmClient/SmokeTest 四处魔数）
        public const int ChatTimeoutMs = 180000;
        public const int VlmTimeoutMs = 120000;

        // 非空=加载出了问题（当前仅"解析失败"），供宿主展示；区分于"未配置"
        public string LoadError = "";

        public static string FindPath()
        {
            string env = Environment.GetEnvironmentVariable("ANLAN_CAD_AGENT_CONFIG");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

            string appdata = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AnlanCadAgent", "config.json");
            if (File.Exists(appdata)) return appdata;

            try
            {
                string beside = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "config.json");
                if (File.Exists(beside)) return beside;
            }
            catch (Exception) { }
            return null;
        }

        public static LlmConfig Load()
        {
            // TLS 1.2 钉死已收口到 HttpJson 静态构造
            string path = FindPath();
            if (path == null) return new LlmConfig();
            try
            {
                var ser = new JsX();
                var d = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                var c = new LlmConfig();
                if (d.ContainsKey("baseUrl") && d["baseUrl"] != null)
                    c.BaseUrl = (string)d["baseUrl"];
                if (d.ContainsKey("apiKey") && d["apiKey"] != null)
                    c.ApiKey = (string)d["apiKey"];
                if (d.ContainsKey("model") && d["model"] != null)
                    c.Model = (string)d["model"];
                if (d.ContainsKey("vlmModel") && d["vlmModel"] != null)
                    c.VlmModel = (string)d["vlmModel"];
                return c;
            }
            catch (Exception ex)
            {
                var bad = new LlmConfig();
                bad.LoadError = "config.json 解析失败: " + ex.Message + "（" + path + "）";
                return bad;
            }
        }

        // 设置窗落盘位置：已有配置沿用它（env→APPDATA→dll旁），没有则默认建 %APPDATA%
        // （教师机规范位置；与 install.ps1 的 -ApiKey 写入路径一致）
        public static string ActivePath()
        {
            string path = FindPath();
            if (path != null) return path;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AnlanCadAgent", "config.json");
        }

        // 写盘（对象构建序列化，禁手写转义 JSON——守则）；目录不存在自动建
        public static void Save(LlmConfig c, string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var d = new Dictionary<string, object>();
            d["baseUrl"] = c.BaseUrl ?? "";
            d["apiKey"] = c.ApiKey ?? "";
            d["model"] = c.Model ?? "";
            d["vlmModel"] = c.VlmModel ?? "";
            File.WriteAllText(path, new JsX().Serialize(d));
        }
    }
}
