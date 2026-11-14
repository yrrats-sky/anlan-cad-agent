// CAD 工具实现：Core 工具名 → AutoCAD 对象模型
// 线程模型：AgentLoop 在工作线程执行工具，所有 CAD API 必须经 Dispatcher.Invoke
// 同步切回主线程（面板 UI 建在 CAD 主线程上，其 Dispatcher 即主线程）——ObjectARX 非线程安全
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AnlanCadCore;

namespace AnlanCadAcadFx48
{
    public static class CadBridge
    {
        private static LlmConfig _cfg;
        private static Dispatcher _ui;

        public static void RegisterAll(ToolRegistry tools, LlmConfig cfg, Dispatcher ui)
        {
            _cfg = cfg;
            _ui = ui;

            tools.Register("draw_line", DrawLine);
            tools.Register("draw_circle", DrawCircle);
            tools.Register("draw_text", DrawText);
            tools.Register("add_dim_aligned", AddDim);
            tools.Register("list_layers", ListLayers);
            tools.Register("create_layer", CreateLayer);
            tools.Register("set_current_layer", SetCurrentLayer);
            tools.Register("zoom_extents", delegate(Dictionary<string, object> a)
            {
                return OnUi(delegate(Document doc)
                {
                    // 选项用裸字母 E（中文"范围(E)"/英文 Extents 缩写同为 E，双语通吃）；
                    // 下划线前缀 _E 中文版不认，实测会卡提示并吞掉后续命令流
                    doc.SendStringToExecute("_.ZOOM E ", true, false, false);
                    return ToolRegistry.JsonOk("已缩放到范围");
                });
            });
            tools.Register("undo_back", delegate(Dictionary<string, object> a)
            {
                return OnUi(delegate(Document doc)
                {
                    // 同上：_BACK 中文版不认（卡在数值提示，LASTPROMPT 滞留的根因，
                    // 还会吞掉后续 SendStringToExecute）；裸 B = 后退(B)/Back 双语通吃
                    doc.SendStringToExecute("_.UNDO B ", true, false, false);
                    return ToolRegistry.JsonOk("已回退到本轮提问前的状态");
                });
            });
            tools.Register("draw_frame_a4", DrawFrame);
            tools.Register("screenshot_and_check", ScreenshotCheck);
        }

        // ---- 基础 ----

        private static Document ActiveDoc()
        {
            return Application.DocumentManager.MdiActiveDocument;
        }

        // 在主线程上执行（无 Transaction 需求的操作）
        private delegate string UiBody(Document doc);

        private static string OnUi(UiBody body)
        {
            string result = null;
            _ui.Invoke(new Action(delegate
            {
                Document doc = ActiveDoc();
                result = (doc == null)
                    ? ToolRegistry.JsonErr("无活动文档")
                    : body(doc);
            }));
            return result;
        }

        private delegate string TxBody(Document doc, Transaction tr, Database db);

        private static string WithTransaction(TxBody body)
        {
            string result = null;
            _ui.Invoke(new Action(delegate
            {
                Document doc = ActiveDoc();
                if (doc == null)
                {
                    result = ToolRegistry.JsonErr("无活动文档");
                    return;
                }
                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    result = body(doc, tr, doc.Database);
                    tr.Commit();
                }
            }));
            return result;
        }

        // 入模型空间并自动建层；返回层信息（"（已自动建层 x）"或空串）供调用方拼进汇报
        private static string Append(Document doc, Transaction tr, Entity e, string layer)
        {
            string info = "";
            if (!string.IsNullOrEmpty(layer))
            {
                info = EnsureLayer(tr, doc.Database, layer);   // 先确保层存在（e.Layer 赋不存在的层名会抛）
            }
            e.SetDatabaseDefaults();          // 先取默认（会覆盖 Layer），再设目标层
            if (!string.IsNullOrEmpty(layer)) e.Layer = layer;
            BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            ms.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
            return info;
        }

        private static string EnsureLayer(Transaction tr, Database db, string name)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
            if (lt.Has(name)) return "";
            LayerTableRecord ltr = new LayerTableRecord();
            ltr.Name = name;
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return "（已自动建层 " + name + "）";
        }

        // ---- 绘图工具 ----

        private static string DrawLine(Dictionary<string, object> a)
        {
            return WithTransaction(delegate(Document doc, Transaction tr, Database db)
            {
                Point3d p1 = new Point3d(ToolRegistry.Num(a, "x1", 0), ToolRegistry.Num(a, "y1", 0), 0);
                Point3d p2 = new Point3d(ToolRegistry.Num(a, "x2", 0), ToolRegistry.Num(a, "y2", 0), 0);
                Line e = new Line(p1, p2);
                string layer = ToolRegistry.Str(a, "layer", "0");
                string info = Append(doc, tr, e, layer);
                return ToolRegistry.JsonOk("已画线 (" + F(p1.X) + "," + F(p1.Y) + ")->("
                    + F(p2.X) + "," + F(p2.Y) + ")" + info);
            });
        }

        private static string DrawCircle(Dictionary<string, object> a)
        {
            return WithTransaction(delegate(Document doc, Transaction tr, Database db)
            {
                Point3d c = new Point3d(ToolRegistry.Num(a, "cx", 0), ToolRegistry.Num(a, "cy", 0), 0);
                double r = ToolRegistry.Num(a, "r", 10);
                Circle e = new Circle(c, Vector3d.ZAxis, r);
                string layer = ToolRegistry.Str(a, "layer", "0");
                string info = Append(doc, tr, e, layer);
                return ToolRegistry.JsonOk("已画圆 圆心(" + F(c.X) + "," + F(c.Y)
                    + ") R=" + F(r) + info);
            });
        }

        private static string DrawText(Dictionary<string, object> a)
        {
            return WithTransaction(delegate(Document doc, Transaction tr, Database db)
            {
                string layer = ToolRegistry.Str(a, "layer", "0");
                string info = Text(doc, tr, ToolRegistry.Num(a, "x", 0), ToolRegistry.Num(a, "y", 0),
                    Math.Max(0.1, ToolRegistry.Num(a, "h", 5)),
                    ToolRegistry.Str(a, "text", ""), layer);
                return ToolRegistry.JsonOk("已写文字: " + ToolRegistry.Str(a, "text", "") + info);
            });
        }

        private static string AddDim(Dictionary<string, object> a)
        {
            return WithTransaction(delegate(Document doc, Transaction tr, Database db)
            {
                Point3d p1 = new Point3d(ToolRegistry.Num(a, "x1", 0), ToolRegistry.Num(a, "y1", 0), 0);
                Point3d p2 = new Point3d(ToolRegistry.Num(a, "x2", 0), ToolRegistry.Num(a, "y2", 0), 0);
                double off = ToolRegistry.Num(a, "offset", 10);
                Vector2d d = new Vector2d(p2.X - p1.X, p2.Y - p1.Y);
                Vector2d n = (d.Length > 0) ? new Vector2d(-d.Y, d.X).GetNormal() : new Vector2d(0, 1);
                Point3d dl = new Point3d(
                    (p1.X + p2.X) / 2 + n.X * off, (p1.Y + p2.Y) / 2 + n.Y * off, 0);
                AlignedDimension dim = new AlignedDimension(p1, p2, dl, "", db.Dimstyle);
                Append(doc, tr, dim, "");
                return ToolRegistry.JsonOk("已标注 " + F(p1) + " 至 " + F(p2));
            });
        }

        // ---- 图框模板（教学简化版 GB A4 横向）----

        private static string DrawFrame(Dictionary<string, object> a)
        {
            return WithTransaction(delegate(Document doc, Transaction tr, Database db)
            {
                double x = ToolRegistry.Num(a, "x", 0);
                double y = ToolRegistry.Num(a, "y", 0);
                string title = ToolRegistry.Str(a, "title", "图样");
                string drawer = ToolRegistry.Str(a, "drawer", "");
                string scale = ToolRegistry.Str(a, "scale", "1:1");
                string layer = ToolRegistry.Str(a, "layer", "图框");

                string info = Append(doc, tr, Rect(x, y, x + 297, y + 210), layer);   // 外框（首个实体顺带建层）
                Append(doc, tr, Rect(x + 25, y + 5, x + 292, y + 205), layer);   // 内框(左25装订边)

                // 简化标题栏 60x20，内框右下角
                double lx = x + 232, ly = y + 5;
                Append(doc, tr, Seg(lx, ly + 12, lx + 60, ly + 12), layer);      // 上下格分隔
                Append(doc, tr, Seg(lx + 20, ly, lx + 20, ly + 12), layer);      // 竖分1
                Append(doc, tr, Seg(lx + 40, ly, lx + 40, ly + 12), layer);      // 竖分2
                Text(doc, tr, lx + 3, ly + 13.5, 4, title, layer);
                Text(doc, tr, lx + 1.5, ly + 5.5, 2.5, "制图 " + drawer, layer);
                Text(doc, tr, lx + 21.5, ly + 5.5, 2.5, "比例 " + scale, layer);
                Text(doc, tr, lx + 41.5, ly + 5.5, 2.5, "日期 ____", layer);

                doc.SendStringToExecute("_.ZOOM E ", true, false, false);   // 裸选项字母，理由同 zoom_extents
                return ToolRegistry.JsonOk("已画A4横版图框（" + title + "），含简化标题栏" + info);
            });
        }

        private static Polyline Rect(double x1, double y1, double x2, double y2)
        {
            Polyline pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(x1, y1), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x2, y1), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x2, y2), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x1, y2), 0, 0, 0);
            pl.Closed = true;
            return pl;
        }

        private static Line Seg(double x1, double y1, double x2, double y2)
        {
            return new Line(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0));
        }

        private static string Text(Document doc, Transaction tr,
            double x, double y, double h, string s, string layer)
        {
            if (s == null || s.Length == 0) return "";
            DBText t = new DBText();
            t.Position = new Point3d(x, y, 0);
            t.Height = h;
            t.TextString = s;
            return Append(doc, tr, t, layer);
        }

        // ---- 图层工具 ----

        private static string ListLayers(Dictionary<string, object> a)
        {
            return WithTransaction(delegate(Document doc, Transaction tr, Database db)
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                var names = new List<string>();
                foreach (ObjectId id in lt)
                {
                    LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    names.Add(ltr.Name + (ltr.Id == db.Clayer ? " [当前]" : ""));
                }
                return ToolRegistry.JsonOk(string.Join(", ", names.ToArray()));
            });
        }

        private static string CreateLayer(Dictionary<string, object> a)
        {
            return WithTransaction(delegate(Document doc, Transaction tr, Database db)
            {
                string name = ToolRegistry.Str(a, "name", "");
                if (name.Length == 0) return ToolRegistry.JsonErr("图层名为空");
                short colorIdx = (short)Math.Max(1, Math.Min(255, ToolRegistry.Num(a, "color", 7)));
                string info = EnsureLayer(tr, db, name);   // 不存在则先建（默认色）
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIdx);
                return ToolRegistry.JsonOk(
                    (info.Length > 0 ? "已建图层: " : "图层已存在，已改色: ")
                    + name + " 颜色ACI" + colorIdx);
            });
        }

        private static string SetCurrentLayer(Dictionary<string, object> a)
        {
            return WithTransaction(delegate(Document doc, Transaction tr, Database db)
            {
                string name = ToolRegistry.Str(a, "name", "");
                if (name.Length == 0) return ToolRegistry.JsonErr("图层名为空");   // 镜像 create_layer，防建空名层
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                string info = "";
                if (!lt.Has(name))
                {
                    info = EnsureLayer(tr, db, name);
                    lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                }
                db.Clayer = lt[name];
                return ToolRegistry.JsonOk("当前层已设为: " + name + info);
            });
        }

        // ---- 截图回环：截CAD主窗口 → VLM 检查 → 文本报告回喂主循环 ----

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);


        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        private static string ScreenshotCheck(Dictionary<string, object> a)
        {
            string question = ToolRegistry.Str(a, "question",
                "检查当前图纸：图形/图框/标注是否规范协调，200字内简短报告，有问题按严重度排序");
            string b64 = null;
            string err = null;   // 与 OnUi/WithTransaction 同风格：错误转 JsonErr，不向上抛
            _ui.Invoke(new Action(delegate
            {
                Document doc = ActiveDoc();
                if (doc == null) { err = "无活动文档"; return; }
                doc.Editor.Regen();   // 同步刷新视图：截图不能早于 SendStringToExecute 排队指令（REGEN/ZOOM）生效
                // 截图前必须置前自己的主窗口：被遮挡时 CopyFromScreen 截到的是覆盖窗口
                //（11-10 实测：acad 后台时 VLM 收到的是聊天窗口，自查跑偏烧光 8 轮）。
                // 置前"本进程自己的窗口"不受 Windows 前台锁限制
                IntPtr hwnd = Application.MainWindow.Handle;
                ShowWindow(hwnd, 9);          // SW_RESTORE：最小化时先还原
                SetForegroundWindow(hwnd);
                System.Threading.Thread.Sleep(300);   // 主线程等窗口切换渲染完成，短暂卡顿可接受
                RECT r;
                if (!GetWindowRect(Application.MainWindow.Handle, out r))
                {
                    err = "GetWindowRect 失败";
                    return;
                }
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                if (w <= 0 || h <= 0) { err = "窗口尺寸异常 " + w + "x" + h; return; }
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, h))
                {
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h));
                    }
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        b64 = Convert.ToBase64String(ms.ToArray());
                    }
                }
            }));
            if (err != null) return ToolRegistry.JsonErr(err);
            // VLM 网络调用留在工作线程，别占主线程
            return ToolRegistry.JsonOk(VlmClient.Ask(_cfg, question, b64));
        }

        // ---- 格式化 ----

        private static string F(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string F(Point3d p)
        {
            return "(" + F(p.X) + "," + F(p.Y) + ")";
        }
    }
}
