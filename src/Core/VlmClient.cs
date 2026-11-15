// 视觉模型客户端：截图回环校验用。图片以 data URL 进 user 消息（OpenAI 兼容格式）
// 独立于主工具循环 → 主循环 tool result 保持纯文本，兼容性无虞
using System;
using System.Collections.Generic;

namespace AnlanCadCore
{
    public static class VlmClient
    {
        public static string Ask(LlmConfig cfg, string question, string pngBase64)
        {
            var textPart = new Dictionary<string, object>();
            textPart["type"] = "text";
            textPart["text"] = question;

            var urlMap = new Dictionary<string, object>();
            urlMap["url"] = "data:image/png;base64," + pngBase64;
            var imgPart = new Dictionary<string, object>();
            imgPart["type"] = "image_url";
            imgPart["image_url"] = urlMap;

            var msg = new Dictionary<string, object>();
            msg["role"] = "user";
            msg["content"] = new object[] { textPart, imgPart };

            var body = new Dictionary<string, object>();
            body["model"] = cfg.VlmModel;
            body["messages"] = new object[] { msg };
            body["temperature"] = 0.1;
            body["max_tokens"] = 600;

            string resp = HttpJson.Post(
                cfg.BaseUrl.TrimEnd('/') + "/chat/completions",
                cfg.ApiKey,
                new JsX().Serialize(body),
                LlmConfig.VlmTimeoutMs);

            var ser = new JsX();
            var root = ser.Deserialize<Dictionary<string, object>>(resp);
            if (root == null) throw new Exception("VLM 响应解析失败: " + HttpJson.Trunc(resp, 300));
            object errObj;
            if (root.TryGetValue("error", out errObj) && errObj != null)
            {
                var err = errObj as Dictionary<string, object>;
                throw new Exception("VLM错误: " + (err != null && err.ContainsKey("message")
                    ? err["message"].ToString() : resp));
            }
            object choicesObj;
            if (!root.TryGetValue("choices", out choicesObj) || choicesObj == null)
                throw new Exception("VLM 响应无 choices");
            // JavaScriptSerializer 数组形态防御（共享 HttpJson.ToArr）
            object[] choices = HttpJson.ToArr(choicesObj);
            if (choices.Length == 0)
                throw new Exception("VLM choices 为空: " + HttpJson.Trunc(resp, 300));
            var c0 = choices[0] as Dictionary<string, object>;
            if (c0 == null) throw new Exception("VLM choices[0] 形态异常");
            object mObj;
            if (!c0.TryGetValue("message", out mObj) || mObj == null)
                throw new Exception("VLM 响应无 message: " + HttpJson.Trunc(resp, 300));
            var m = mObj as Dictionary<string, object>;
            if (m == null) throw new Exception("VLM message 形态异常");
            return m.ContainsKey("content") && m["content"] != null ? m["content"].ToString() : "";
        }
    }
}
