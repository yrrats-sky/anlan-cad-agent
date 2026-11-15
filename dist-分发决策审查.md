# anlan-cad-agent 分发决策审查（决策终态速查）

> 2026-11-11 侧聊产出；2026-11-15 按"更新即删除"守则压缩为终态速查（论证过程与已废止历史已删）。
> 全量落地 spec = dist-exe安装包落地方案.md；执行进度 = HANDOFF.md。

## 决策终态（7 项；✅ = 已落地验收）

1. **装载机制** ✅：HKCU 按需加载键 + Roaming Support acaddoc.lsp 钩子（免管理员；别家 S::STARTUP 只警告不合并，地推人工处理；绿色版无注册表 = "暂不支持"话术）。标准 C:\Program Files 真机、R25/R26 前向兼容待真机（进行中）
2. **版本分级** ✅：R24.2 全量；R24.0+/R25/R26 注册但标"兼容模式"；R23- 不支持；探测结果 -ReportUrl 回传站点
3. **更新机制**（待做）：面板内检查更新 + 从 /downloads/ 拉新 bundle；话术必须"先存图关 CAD 再装"（dll 锁定）
4. **分发形态** ✅（11-14 改判后已落地）：Inno exe 主 + zip 备用；GitHub Releases 托管（repo yrrats-sky/anlan-cad-agent）+ 站内镜像双链接；OV 不买（触发 = 官网自助流失数据可测且难看）
5. **WebView2 降级** ✅：运行时预检 / catch / 失败事件 / 10s 看门狗四层 → WPF 兜底（新 GUID + 浮动不 Dock）
6. **key 双轨** ✅：官网轨无 key 面板首开引导 ⚙ / 地推轨烘焙归因；串 key 已修（[InstallDelete] + install.ps1 双向同步双保险）
7. **安装时从站点拉默认模型**（待做）：install.ps1 现硬编码 glm-5.3 / sensenova-6.8-flash，应拉站点端点、失败才落本地兜底

## 站点侧配合（[[anlanai-relay]]）

| 项 | 状态/时机 |
|---|---|
| /downloads/anlan-cad/ 发布（exe + zip + version.json + no-cache + 图文卡） | ✅ 已上线，公网验证（sha256 一致） |
| QuotaForNewUser 100000 ≈ 2667 token → 提到 20-50 轮量级 | **未做，官网轨生死门** |
| install/heartbeat 端点（-ReportUrl 已就位） | 未做，试点中做 |
| OV 签名 | 缓议（触发 = 自助流失数据难看） |

## 不做清单

机房机全权限 / Win7 / 32 位 / 绿色版 CAD / R21-R23 / MSIX·ClickOnce / 重型常驻启动器 / 「双击安装.bat」改名
