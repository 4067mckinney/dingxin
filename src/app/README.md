# 定心 1.19.11 源码包

## 版本

- 程序版本：1.19.11.0（`Program.cs` 中 `AssemblyVersion`）
- 基线：专注守门 1.19.10
- 构建日期：2026-11-24

## 目录说明

| 文件 | 作用 |
|---|---|
| `Program.cs` | 入口、安装/卸载、崩溃日志、备份路径 |
| `ProductBrand.cs` / `BrandArtwork.cs` | 产品名称、旧版识别和品牌标识绘制 |
| `Engine.cs` | 状态、设置、专注会话、网站解析 |
| `MainWindow*.cs` | 主窗口：布局、规则、设置、统计、记录、弹窗 |
| `SetupWindow.cs` | 安装器界面 + `WindowPalette` 主题色 |
| `FloatingTimerWindow.cs` | 悬浮计时窗 |
| `AppImages.cs` | 软件图标缓存（LRU 上限 96） |
| `AppCatalog.cs` / `ProcessGuard.cs` | 已识别软件、进程/窗口监视 |
| `FocusHistory.cs` / `AdvancedCredential.cs` | 专注记录、高级密码 |
| `FocusVerification.cs` | 自带验收用例（`-Verify`） |
| `Focus.ico` / `StudyFocus.manifest` | 图标与清单（嵌入 exe） |
| `Build.ps1` | 一键构建脚本 |

## 构建

需要 Windows 自带的 .NET Framework 4.x 编译器（无需 Visual Studio / .NET SDK）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -OutputDirectory .\out
```

产物为 `out\StudyFocus_Setup.exe`。发布包使用「定心」作为显示名称。

构建脚本会：
1. 用 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 编译；
2. 嵌入 `Focus.ico` 与 `StudyFocus.manifest`；
3. 可选签名（`-CertificateThumbprint` 参数，未提供则跳过）。

## 改版本号

发布新版本时**必须**修改 `Program.cs` 顶部的：

```csharp
[assembly: AssemblyVersion("1.19.11.0")]
```

否则安装器会认为“已安装相同版本”，只启动旧程序而不覆盖（1.18.x 阶段踩过这个坑，1.18.18 起同版本会弹窗询问是否覆盖）。

## 已知限制

- 未做 Authenticode 签名；
- 清单为 System DPI aware，未启用 PerMonitorV2；
- 弹窗标题栏为系统原生（仅染色）；
- 网站搜索/添加框为原生 TextBox（已主题化）。

## 数据位置

- 状态：`%LOCALAPPDATA%\StudyFocus\state.dat`（DPAPI 加密）
- 专注记录：`%LOCALAPPDATA%\StudyFocus\FocusHistory\yyyy-MM.jsonl`
- 崩溃日志：`%LOCALAPPDATA%\StudyFocus\crash-*.log`（保留 20 个）

安装器可识别「专注守门」和「定心」的现有安装，继续使用原安装目录与数据目录。从旧名升级后，请使用与此包配套的应急停止工具。
