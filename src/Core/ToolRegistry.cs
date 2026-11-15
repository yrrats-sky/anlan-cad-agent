// 工具注册表：schema 定义（喂给 LLM）+ 执行分发（宿主注入实现）
// 宿主无关：CAD 版注册 CadBridge 实现，console 冒烟版注册 mock 实现
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AnlanCadCore
{
    public class ToolRegistry
    {
        private readonly Dictionary<string, Func<Dictionary<string, object>, string>> _tools
            = new Dictionary<string, Func<Dictionary<string, object>, string>>();

        // schema 单一事实源：对象构建（防手写转义天书，历史上炸过一次），字符串版派生自它
        private static readonly object[] _schemaObjs = BuildSchemaObjects();

        // 兼容消费方（SmokeTest 自检等）：字符串形态
        public static readonly string[] Schemas = BuildSchemaStrings();

        // 供 BuildBody 直接内联，免每轮反序列化
        public static object[] SchemaObjects { get { return _schemaObjs; } }

        private static object[] BuildSchemaObjects()
        {
            return new object[]
            {
                S("draw_line", "画一条直线段", new Dictionary<string, object>
                {
                    { "x1", P("number", "起点X") },
                    { "y1", P("number", "起点Y") },
                    { "x2", P("number", "终点X") },
                    { "y2", P("number", "终点Y") },
                    { "layer", P("string", "目标图层名，不存在会自动创建，缺省为0层") }
                }, "x1", "y1", "x2", "y2"),
                S("draw_circle", "画一个圆", new Dictionary<string, object>
                {
                    { "cx", P("number", "圆心X") },
                    { "cy", P("number", "圆心Y") },
                    { "r", P("number", "半径") },
                    { "layer", P("string", null) }
                }, "cx", "cy", "r"),
                S("draw_text", "写单行文字", new Dictionary<string, object>
                {
                    { "x", P("number", null) },
                    { "y", P("number", null) },
                    { "h", P("number", "字高") },
                    { "text", P("string", "文字内容") },
                    { "layer", P("string", null) }
                }, "x", "y", "h", "text"),
                S("add_dim_aligned", "添加对齐尺寸标注（两点间）", new Dictionary<string, object>
                {
                    { "x1", P("number", null) },
                    { "y1", P("number", null) },
                    { "x2", P("number", null) },
                    { "y2", P("number", null) },
                    { "offset", P("number", "尺寸线离两点的偏移距离，缺省10") }
                }, "x1", "y1", "x2", "y2"),
                S("list_layers", "列出所有图层名及当前层",
                    new Dictionary<string, object>(), null),
                S("create_layer", "新建图层并设颜色（ACI颜色索引1红2黄3绿4青5蓝6品红7白）", new Dictionary<string, object>
                {
                    { "name", P("string", null) },
                    { "color", P("integer", "ACI颜色索引，缺省7") }
                }, "name"),
                S("set_current_layer", "把某图层设为当前层", new Dictionary<string, object>
                {
                    { "name", P("string", null) }
                }, "name"),
                S("zoom_extents", "视图缩放到全部实体范围",
                    new Dictionary<string, object>(), null),
                S("undo_back", "一键撤销本助手本轮的全部绘图操作（回到最近一次用户提问前的状态）。一次即可，勿连续调用",
                    new Dictionary<string, object>(), null),
                S("draw_frame_a4", "画A4横版GB图框（297x210，左侧25装订边，含简化标题栏）", new Dictionary<string, object>
                {
                    { "x", P("number", "图框原点X，缺省0") },
                    { "y", P("number", "图框原点Y，缺省0") },
                    { "title", P("string", "图样名称") },
                    { "drawer", P("string", "制图人姓名，可空") },
                    { "scale", P("string", "比例，缺省1:1") }
                }, "title"),
                S("screenshot_and_check", "截取当前CAD窗口图像并交给视觉模型检查，返回规范性报告。画完一组图形后建议调用一次自查", new Dictionary<string, object>
                {
                    { "question", P("string", "检查重点，如：图框是否完整、标注是否重叠") }
                }, null)
            };
        }

        // schema 构建器：function 三段包装（type/name/description/parameters）
        private static Dictionary<string, object> S(string name, string desc,
            Dictionary<string, object> props, params string[] required)
        {
            var parameters = new Dictionary<string, object>();
            parameters["type"] = "object";
            parameters["properties"] = props;
            if (required != null && required.Length > 0) parameters["required"] = required;
            var fn = new Dictionary<string, object>();
            fn["name"] = name;
            fn["description"] = desc;
            fn["parameters"] = parameters;
            var root = new Dictionary<string, object>();
            root["type"] = "function";
            root["function"] = fn;
            return root;
        }

        // 属性构建器：type + 可选 description
        private static Dictionary<string, object> P(string type, string desc)
        {
            var d = new Dictionary<string, object>();
            d["type"] = type;
            if (desc != null) d["description"] = desc;
            return d;
        }

        private static string[] BuildSchemaStrings()
        {
            var ser = new JsX();
            var arr = new string[_schemaObjs.Length];
            for (int i = 0; i < _schemaObjs.Length; i++)
            {
                arr[i] = ser.Serialize(_schemaObjs[i]);
            }
            return arr;
        }

        public void Register(string name, Func<Dictionary<string, object>, string> fn)
        {
            _tools[name] = fn;
        }

        // argsJson 在此处解析：失败转 JsonErr 回喂 LLM（由它纠正），
        // 而不是静默回空字典让工具按缺省值"成功"执行（曾致 draw_line 画 (0,0)→(0,0) 零线）
        public string Execute(string name, string argsJson)
        {
            Func<Dictionary<string, object>, string> fn;
            if (!_tools.TryGetValue(name, out fn))
            {
                return JsonErr("未知工具: " + name);
            }
            Dictionary<string, object> args;
            try
            {
                args = ParseArgs(argsJson);
            }
            catch (Exception ex)
            {
                return JsonErr(name + ": 参数解析失败: " + ex.Message);
            }
            try
            {
                return fn(args);
            }
            catch (Exception ex)
            {
                return JsonErr(name + ": " + ex.Message);   // 带工具名，回喂 LLM 后可排障
            }
        }

        public static string JsonOk(string dataJsonOrText)
        {
            return "{\"ok\":true,\"data\":" + Quote(dataJsonOrText) + "}";
        }

        public static string JsonErr(string msg)
        {
            return "{\"ok\":false,\"error\":" + Quote(msg) + "}";
        }

        public static string Quote(string s)
        {
            if (s == null) return "\"\"";
            var sb = new System.Text.StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u" + ((int)c).ToString("x4"));   // 其余控制符(CAD 异常消息可能夹带)
                else sb.Append(c);
            }
            sb.Append("\"");
            return sb.ToString();
        }

        // ---- 参数取值 helper（JavaScriptSerializer 数值可能是 int/decimal/double）----
        public static double Num(Dictionary<string, object> a, string k, double def)
        {
            object v;
            if (!a.TryGetValue(k, out v) || v == null) return def;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return def; }
        }

        public static string Str(Dictionary<string, object> a, string k, string def)
        {
            object v;
            if (!a.TryGetValue(k, out v) || v == null) return def;
            return v.ToString();
        }

        // 解析失败直接抛（由 Execute 统一转 JsonErr）；空串=无参工具，回空字典
        public static Dictionary<string, object> ParseArgs(string json)
        {
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, object>();
            var ser = new JsX();
            return ser.Deserialize<Dictionary<string, object>>(json);
        }
    }
}
