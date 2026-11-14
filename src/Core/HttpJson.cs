// HTTP/JSON 通道：AgentLoop 与 VlmClient 共用。TLS 1.2 钉死逻辑也在这里收口
using System;
using System.IO;
using System.Net;
using System.Text;

namespace AnlanCadCore
{
    public static class HttpJson
    {
        static HttpJson()
        {
            EnsureTls();
            // 中转站为自有阿里云服务器，直连稳定可达；系统代理（Clash/v2ray 等）
            // 节点异常时会拖死请求（TLS 握手无响应）。默认绕过系统代理直连；
            // 日后若需走代理，在 config.json 加 proxy 字段显式放行。
            WebRequest.DefaultWebProxy = new WebProxy();
        }

        public static void EnsureTls()
        {
            // csc 直编产物无 TargetFrameworkAttribute，.NET 4.8 会按旧式应用降级到
            // TLS1.0/SSL3，现代网关直接握手失败——显式钉死 TLS 1.2+
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072
                    | (SecurityProtocolType)12288;   // Tls12 | Tls13
            }
            catch (Exception) { }
        }

        public static string Post(string url, string apiKey, string bodyJson, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Headers["Authorization"] = "Bearer " + apiKey;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "AnlanCadAgent/0.3";
            // 站点层反滥用会把 API 请求 302 到登录页——禁自动跟随，让重定向原样暴露
            req.AllowAutoRedirect = false;

            byte[] data = Encoding.UTF8.GetBytes(bodyJson);
            req.ContentLength = data.Length;
            using (Stream s = req.GetRequestStream())
            {
                s.Write(data, 0, data.Length);
            }
            return ReadOrThrow(req, 400);
        }

        public static string Get(string url, string apiKey, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Headers["Authorization"] = "Bearer " + apiKey;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            // 与 Post 同款硬化：防 302 到登录页、UA 标识
            req.UserAgent = "AnlanCadAgent/0.3";
            req.AllowAutoRedirect = false;
            return ReadOrThrow(req, 300);
        }

        // 读响应体；WebException 时尽力读出错误页正文（截断）后抛带状态码的异常
        private static string ReadOrThrow(HttpWebRequest req, int errTruncLen)
        {
            try
            {
                using (HttpWebResponse w = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(w.GetResponseStream(), Encoding.UTF8))
                {
                    return r.ReadToEnd();
                }
            }
            catch (WebException we)
            {
                string detail = "";
                HttpWebResponse er = we.Response as HttpWebResponse;
                if (er != null)
                {
                    using (var r = new StreamReader(er.GetResponseStream(), Encoding.UTF8))
                    {
                        detail = Trunc(r.ReadToEnd(), errTruncLen);
                    }
                }
                throw new Exception("HTTP " + (er != null ? (int)er.StatusCode : 0) + ": " + detail);
            }
        }

        // 安全截断（供全 Core 层引用；原在 AgentLoop，因基础设施不应反向依赖引擎层迁入此处）
        public static string Trunc(string s, int n)
        {
            if (s == null) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        // JavaScriptSerializer 数组形态防御：object[]/ArrayList/其他 IEnumerable 一律转 object[]
        // （原 AgentLoop.ToArr；第三处消费出现前按守则提取共享——解析任何 JSS 数组必须走它）
        public static object[] ToArr(object o)
        {
            if (o is object[]) return (object[])o;
            System.Collections.IEnumerable e = o as System.Collections.IEnumerable;
            if (e == null) return new object[0];
            var list = new System.Collections.Generic.List<object>();
            foreach (object x in e) list.Add(x);
            return list.ToArray();
        }
    }
}
