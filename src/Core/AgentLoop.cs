// Agent 循环：user 输入 → LLM → tool_calls → 执行 → 回喂 → … → 最终文本
// 宿主无关：不引用 CAD 程序集。线程回调 OnMessage（非 UI 线程，宿主自行 Dispatcher）
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace AnlanCadCore
{
    public class AgentLoop
    {
        private readonly LlmConfig _cfg;
        private readonly ToolRegistry _tools;
        private readonly List<Dictionary<string, object>> _messages
            = new List<Dictionary<string, object>>();

        public event Action<string, string> OnMessage;   // (role, text)
        public event Action Completed;                    // 一轮结束（成功或出错）

        public const int MaxRounds = 8;

        private const string SystemPrompt =
            "你是内嵌在 AutoCAD 里的制图助教（安岚教育版），用户是职校师生。" +
            "规则：1)每次只调用一个工具，等拿到结果再决定下一步；" +
            "2)单位一律毫米，用户没说尺寸就先问清再画；" +
            "3)清空/覆盖类破坏性操作先向用户确认；" +
            "4)回答简短口语化，画完后用一句话汇报结果。";

        public AgentLoop(LlmConfig cfg, ToolRegistry tools)
        {
            _cfg = cfg;
            _tools = tools;
            SeedSystem();
        }

        public void Reset()
        {
            _messages.Clear();
            SeedSystem();
        }

        private void SeedSystem()
        {
            var m = new Dictionary<string, object>();
            m["role"] = "system";
            m["content"] = SystemPrompt;
            _messages.Add(m);
        }

        public void Send(string userText)
        {
            Thread t = new Thread(new ParameterizedThreadStart(RunInner));
            t.IsBackground = true;
            t.Start(userText);
        }

        private void RunInner(object o)
        {
            string userText = (string)o;
            try
            {
                Emit("user", userText);
                var um = new Dictionary<string, object>();
                um["role"] = "user";
                um["content"] = userText;
                _messages.Add(um);

                var ser = new JsX();
                bool gotFinal = false;
                for (int round = 0; round < MaxRounds; round++)
                {
                    string resp = PostChatWithRetry(ser);
                    Dictionary<string, object> msg = ParseChoiceMessage(resp, ser);

                    object tcsObj;
                    if (msg.TryGetValue("tool_calls", out tcsObj) && tcsObj != null)
                    {
                        if (!ExecuteToolCalls(msg)) break;   // 空 tool_calls 数组：放弃本轮
                        continue;   // 带着工具结果再问下一轮
                    }

                    string content = msg.ContainsKey("content") && msg["content"] != null
                        ? msg["content"].ToString() : "";
                    var am = new Dictionary<string, object>();
                    am["role"] = "assistant";
                    am["content"] = content;
                    _messages.Add(am);
                    Emit("assistant", content);
                    gotFinal = true;
                    break;
                }
                if (!gotFinal)   // 每轮都返回 tool_calls 直到耗尽：给用户一句交代，别静默
                    Emit("sys", "连续 " + MaxRounds + " 轮都在调用工具未得出结论，已停止；请把任务拆小再试。");
            }
            catch (Exception ex)
            {
                Emit("error", ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (Completed != null) Completed();
            }
        }

        // 执行一轮 tool_calls：assistant 原样回塞（保 tool_call id），逐个执行并回喂 tool 消息
        // 返回 false 表示 tool_calls 为空形态，调用方应放弃本轮
        private bool ExecuteToolCalls(Dictionary<string, object> msg)
        {
            _messages.Add(msg);
            object[] tcs = ToArr(msg["tool_calls"]);
            if (tcs.Length == 0) return false;
            foreach (object tcObj in tcs)
            {
                var tc = tcObj as Dictionary<string, object>;
                if (tc == null) continue;
                string id = GetStr(tc, "id");
                var fnDict = tc.ContainsKey("function")
                    ? tc["function"] as Dictionary<string, object> : null;
                if (fnDict == null) continue;
                string fn = GetStr(fnDict, "name");
                string argsJson = GetStr(fnDict, "arguments");

                string result = _tools.Execute(fn, argsJson);
                Emit("tool", fn + " → " + HttpJson.Trunc(result, 220));

                var tm = new Dictionary<string, object>();
                tm["role"] = "tool";
                tm["tool_call_id"] = id;
                tm["content"] = result;
                _messages.Add(tm);
            }
            return true;
        }

        // 中转站上游限流是间歇性的，且 WAF 会用 HTTP 200 + HTML 错误页伪装：
        // 异常和响应体两条路都做限流判定，指数退避（8/15/30s）后重试
        private string PostChatWithRetry(JsX ser)
        {
            string url = _cfg.BaseUrl.TrimEnd('/') + "/chat/completions";
            string body = BuildBody(ser);
            int[] backoff = new int[] { 8000, 15000, 30000 };
            for (int attempt = 0; ; attempt++)
            {
                string resp;
                try
                {
                    resp = HttpJson.Post(url, _cfg.ApiKey, body, LlmConfig.ChatTimeoutMs);
                }
                catch (Exception ex)
                {
                    if (IsRate(ex.Message) && attempt < backoff.Length)
                    {
                        BackoffOnce(attempt, backoff, "上游限流");
                        continue;
                    }
                    throw;
                }
                if (attempt < backoff.Length && ShouldBackoff(resp, ser))
                {
                    BackoffOnce(attempt, backoff, "上游瞬态异常(限流/重定向)");
                    continue;
                }
                if (resp.IndexOf("\"choices\"") < 0)
                    throw new Exception("中转站返回非预期响应: " + HttpJson.Trunc(resp, 200));
                return resp;
            }
        }

        private void BackoffOnce(int attempt, int[] backoff, string reason)
        {
            Emit("sys", reason + "，第 " + (attempt + 1) + "/3 次退避重试（"
                + (backoff[attempt] / 1000) + "s）…");
            Thread.Sleep(backoff[attempt]);
        }

        // 含 choices 的合法 JSON 放行（交 ParseChoiceMessage 判错），避免正文聊到"429"
        // 被子串匹配误判触发退避；HTML 错误页/无 choices/解析失败 → 退避
        private static bool ShouldBackoff(string resp, JsX ser)
        {
            if (resp.IndexOf("\"choices\"") < 0) return true;
            try { return ser.DeserializeObject(resp) == null; }
            catch (Exception) { return true; }
        }

        private static bool IsRate(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.IndexOf("429") >= 0
                || s.IndexOf("Too Many Requests", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("rate_limit", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("tpm/rpm", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // 会话历史只增不减曾实测撞 429（大 prompt）：请求前按 user 边界裁剪，
        // 只保 system + 最近 KeepUserTurns 轮；从 user 头裁绝不拆散 tool_calls/tool 配对
        private const int KeepUserTurns = 10;

        private void TrimHistory()
        {
            var userIdx = new List<int>();
            for (int i = 1; i < _messages.Count; i++)
            {
                object role;
                if (_messages[i].TryGetValue("role", out role) && (string)role == "user")
                    userIdx.Add(i);
            }
            if (userIdx.Count <= KeepUserTurns) return;
            int cut = userIdx[userIdx.Count - KeepUserTurns];
            _messages.RemoveRange(1, cut - 1);
        }

        private string BuildBody(JsX ser)
        {
            TrimHistory();
            object[] schemas = ToolRegistry.SchemaObjects;   // 静态已构建，免每轮反序列化
            var body = new Dictionary<string, object>();
            body["model"] = _cfg.Model;
            body["messages"] = _messages;
            body["tools"] = schemas;
            body["temperature"] = 0.2;
            return ser.Serialize(body);
        }

        private static Dictionary<string, object> ParseChoiceMessage(
            string resp, JsX ser)
        {
            var root = ser.Deserialize<Dictionary<string, object>>(resp);
            if (root == null) throw new Exception("LLM 响应解析失败");
            object errObj;
            if (root.TryGetValue("error", out errObj) && errObj != null)
            {
                var err = errObj as Dictionary<string, object>;
                throw new Exception("API错误: " +
                    (err != null && err.ContainsKey("message") ? err["message"].ToString() : resp));
            }
            object choicesObj;
            if (!root.TryGetValue("choices", out choicesObj) || choicesObj == null)
                throw new Exception("响应无 choices: " + HttpJson.Trunc(resp, 300));
            object[] choices = ToArr(choicesObj);
            if (choices.Length == 0)
                throw new Exception("choices 为空: " + HttpJson.Trunc(resp, 500));
            var choice0 = choices[0] as Dictionary<string, object>;
            var msg = choice0 != null && choice0.ContainsKey("message")
                ? choice0["message"] as Dictionary<string, object> : null;
            if (msg == null) throw new Exception("响应无 message");
            return msg;
        }

        private void Emit(string role, string text)
        {
            if (OnMessage != null) OnMessage(role, text);
        }

        private static string GetStr(Dictionary<string, object> d, string k)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v != null) return v.ToString();
            return "";
        }

        // JavaScriptSerializer 数组形态防御（实现已提取共享至 HttpJson.ToArr，语义原样）
        private static object[] ToArr(object o)
        {
            return HttpJson.ToArr(o);
        }
    }
}
