// JSON 序列化门面：API 表面对齐 JavaScriptSerializer 的三方法子集（Serialize /
// Deserialize<T> / DeserializeObject），全项目唯一入口（守则：JSON 不再散点直用）。
// FX48 → 内部转发 JavaScriptSerializer，行为原样；其余（net10.0-windows）→
// System.Text.Json 框架内置（JavaScriptSerializer 的 System.Web.Extensions 是
// Framework-only 程序集，2027 的 .NET 10 宿主里不存在）。
// 反序列化形态契约（两侧一致，消费代码零改动）：object→Dictionary<string,object> /
// array→object[] / 整数→long（Fx48 为 int，消费点只 Convert.ToDouble/ToString，等价）/
// 小数→double / bool→bool / string→string / null→null。
using System;
using System.Collections.Generic;

namespace AnlanCadCore
{
    public class JsX
    {
#if FX48
        private readonly System.Web.Script.Serialization.JavaScriptSerializer _ser
            = new System.Web.Script.Serialization.JavaScriptSerializer();

        public string Serialize(object o) { return _ser.Serialize(o); }

        public T Deserialize<T>(string s) where T : Dictionary<string, object>
        {
            return _ser.Deserialize<T>(s);
        }

        public Dictionary<string, object> DeserializeObject(string s)
        {
            return _ser.DeserializeObject(s) as Dictionary<string, object>;
        }
#else
        private static readonly System.Text.Json.JsonSerializerOptions _opts
            = new System.Text.Json.JsonSerializerOptions
            {
                // 中文/控制字符不转义（与 JavaScriptSerializer 输出习惯一致，体积小可读）
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

        public string Serialize(object o)
        {
            System.Text.Json.Nodes.JsonNode n = ToNode(o);
            return n == null ? "null" : n.ToJsonString(_opts);
        }

        public T Deserialize<T>(string s) where T : Dictionary<string, object>
        {
            return (T)(object)DeserializeObject(s);
        }

        public Dictionary<string, object> DeserializeObject(string s)
        {
            System.Text.Json.Nodes.JsonNode n = System.Text.Json.Nodes.JsonNode.Parse(s);
            return FromNode(n) as Dictionary<string, object>;
        }

        private static System.Text.Json.Nodes.JsonNode ToNode(object o)
        {
            if (o == null) return null;
            if (o is string) return System.Text.Json.Nodes.JsonValue.Create((string)o);
            if (o is bool) return System.Text.Json.Nodes.JsonValue.Create((bool)o);
            if (o is int || o is long || o is short || o is byte)
                return System.Text.Json.Nodes.JsonValue.Create(Convert.ToInt64(o));
            if (o is double || o is float || o is decimal)
                return System.Text.Json.Nodes.JsonValue.Create(Convert.ToDouble(o));
            var d = o as Dictionary<string, object>;
            if (d != null)
            {
                var obj = new System.Text.Json.Nodes.JsonObject();
                foreach (var kv in d) obj[kv.Key] = ToNode(kv.Value);
                return obj;
            }
            System.Collections.IEnumerable e = o as System.Collections.IEnumerable;
            if (e != null)
            {
                var arr = new System.Text.Json.Nodes.JsonArray();
                foreach (object x in e) arr.Add(ToNode(x));
                return arr;
            }
            throw new ArgumentException("JsX.Serialize 不支持类型: " + o.GetType().Name);
        }

        private static object FromNode(System.Text.Json.Nodes.JsonNode n)
        {
            if (n == null) return null;
            var v = n as System.Text.Json.Nodes.JsonValue;
            if (v != null)
            {
                string s; bool b; long l; double dd;
                if (v.TryGetValue<string>(out s)) return s;
                if (v.TryGetValue<bool>(out b)) return b;
                if (v.TryGetValue<long>(out l)) return l;
                if (v.TryGetValue<double>(out dd)) return dd;
                return n.ToJsonString();
            }
            var obj = n as System.Text.Json.Nodes.JsonObject;
            if (obj != null)
            {
                var d = new Dictionary<string, object>();
                foreach (var kv in obj) d[kv.Key] = FromNode(kv.Value);
                return d;
            }
            var arr = n as System.Text.Json.Nodes.JsonArray;
            if (arr != null)
            {
                var list = new List<object>();
                foreach (System.Text.Json.Nodes.JsonNode x in arr) list.Add(FromNode(x));
                return list.ToArray();
            }
            return null;
        }
#endif
    }
}
