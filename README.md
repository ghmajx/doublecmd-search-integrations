# Double Commander Search Integrations

这是一个面向 Windows 的 Double Commander 搜索集成项目，包含两部分：

- `Lertaro/`：Lertaro 原生插件；
- `Listary/`：Listary 自定义命令桥接脚本。

Lertaro 部分的目标是让 Lertaro 在 Double Commander 中获得与官方 Total Commander 适配相同的核心体验：

- 识别 Double Commander 主窗口和文件列表；
- 读取当前活动面板的路径，作为 Lertaro 的局部搜索范围；
- 支持内联搜索停靠和 Quick Navigation；
- 执行搜索结果时回到 Double Commander，并在当前面板打开目录或选中文件；
- 枚举可见 Double Commander 窗口中的路径，供 Lertaro 的打开文件夹能力使用。

Double Commander 没有 Total Commander 那种可用于“查询当前面板路径”的 WM_COPYDATA 协议。路径读取优先使用可暴露的窗口元数据；由于 Double Commander 的路径栏在 Lazarus/Windows 版本中可能是无 HWND 的图形控件，插件还会在活动文件列表、窗口处于前台且命令行为空时调用官方 `cm_AddPathToCmdLine`（默认快捷键 `Ctrl+P`）读取当前路径，随后恢复空命令行。代码不扫描文件系统，也不通过 `Directory.Exists` 判断结果，避免管理员权限和映射盘造成误判。

## 构建

需要 .NET 10 SDK。最稳妥的方式是使用官方 Lertaro 源码中的 `PluginSdk`：

```powershell
dotnet build .\Lertaro\DoubleCommander.csproj -c Release -p:LertaroRoot="C:\src\Lertaro"
```

如果只有已安装的 SDK DLL，也可以这样构建：

```powershell
dotnet build .\Lertaro\DoubleCommander.csproj -c Release -p:LertaroSdkDll="C:\path\to\Lertaro.PluginSdk.dll"
```

指定 `LertaroRoot` 时，构建完成后会自动复制到：

```text
<LertaroRoot>\App\bin\Release\net10.0-windows\Plugins\DoubleCommander\
```

也可以把 `bin\Release\net10.0-windows\Lertaro.Plugins.DoubleCommander.dll` 手动复制到 Lertaro 的 `Plugins\DoubleCommander\` 目录，重启 Lertaro 后在插件设置中确认加载。

## 自检

路径识别规则不依赖正在运行的 Double Commander，可直接运行：

```powershell
dotnet run --project .\tests\DoubleCommander.Heuristics.Tests.csproj
```

真正的集成验收仍需要在本机启动 Double Commander，分别测试：普通路径、UNC 路径、中文路径、左右面板切换、标签页、管理员启动、映射盘和超长路径。若 Double Commander 命令行被隐藏、命令行中已有文字，或窗口不在前台，插件会放弃该次局部路径读取，避免打断用户输入。

## Listary 集成边界

Listary 目前没有可验证的、类似 Double Commander/Total Commander 插件 API 的公开原生插件 SDK。因此本项目不伪装成“Listary 原生插件”，而是使用 Listary 官方支持的自定义命令完成同一件实用的事：把当前目录或输入的路径交给 Double Commander 打开。

Listary 官方 Commands 支持 `"{query}"` 和 `"{current_folder}"` 参数；桥接脚本见 `Listary/DoubleCommander.Listary.ps1`。它不依赖 Listary v7 的测试版 HTTP API，因此版本变化时影响更小。

### 配置 Listary

在 Listary 的 `Options -> Commands` 中新增一条命令：

| 字段 | 值 |
| --- | --- |
| Scope | `File Explorer` |
| Keyword | `dc` |
| Path | `pwsh.exe` |
| Parameter | `-NoProfile -File "C:\path\to\DoubleCommander.Listary.ps1" -Query "{query}" -CurrentFolder "{current_folder}" -Pane R` |

然后在资源管理器中输入 `dc report.pdf`，脚本会使用当前目录拼接相对路径；输入绝对路径则直接打开该路径。Double Commander 不在 `PATH` 时，在参数末尾增加 `-DoubleCommanderPath "D:\tools\Programs\doublecmd\doublecmd.exe"`。

脚本默认使用右面板；需要左面板时将 `-Pane R` 改为 `-Pane L`。如果希望在 Listary 全局搜索窗口使用它，请传入绝对路径，因为全局搜索不一定提供 `{current_folder}`。

桥接脚本支持 `-DryRun`，可在不启动 Double Commander 的情况下检查参数：

```powershell
pwsh -NoProfile -File .\Listary\DoubleCommander.Listary.ps1 `
  -Query 'report.pdf' -CurrentFolder 'C:\Work' -DryRun
```

这不是 Listary 原生插件；如果 Listary 后续公开稳定的插件 SDK，可以在此目录增加真正的适配器，而无需改变 Lertaro 插件。

参考：

- [Lertaro 开发者指南](https://lertaro.github.io/dev-guide/)
- [Double Commander 命令行](https://doublecmd.github.io/doc/en/commandline.html)
- [Double Commander 插件开发说明](https://github.com/doublecmd/doublecmd/wiki/Plugins-development)
- [Listary 官方帮助中心](https://help.listary.com/)
- [Listary v7 本机 HTTP API 说明](https://www.listary.com/v7)
