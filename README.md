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

Lazarus/LCL 还有两个必须绕开的行为，插件已按此实现：

- 文件面板在 Double Commander 1.2 中是普通 `Window` 类控件，而不是 `LCLListBox`/`TMyListBox` 之类的专用类。这类控件只有在确认属于 Double Commander 主窗口、不是编辑类控件且尺寸达到面板规模时才会被当作文件列表，避免误判工具栏与命令行。
- 命令行组合框的文本不写入 USER32 缓存，跨进程 `GetWindowText`/`SetWindowText` 读不到也改不了它。读取和清空都改用带超时的 `WM_GETTEXT`/`WM_SETTEXT` 消息，超时或投递失败时回退到旧的窗口文本 API，目标进程无响应时不会卡住 Lertaro。
- 触发 `cm_AddPathToCmdLine` 不使用 `keybd_event`/`SendInput`：那类 API 会把快捷键注入**当前前台窗口**，实测在焦点切换的竞态下会泄漏到浏览器（弹出打印对话框）或在别的输入框留下裸 `p`。改为把 `WM_KEYDOWN`/`WM_KEYUP` 直接发给 Double Commander 的目标窗口，并用 `AttachThreadInput` + `SetKeyboardState` 让该线程的 `GetKeyState` 认为 Ctrl 处于按下状态，消息只可能被 Double Commander 收到。
- 内联搜索窗的停靠矩形按优先级取：Lertaro 传入的窗口句柄（键盘钩子会把唤起时的焦点面板作为活动窗口广播出来，用 Tab 切换面板后依然准确）→ 当前焦点面板 → 最近发布的活动面板 → 鼠标所在的面板（摆放窗口时焦点已经移到搜索窗上，Double Commander 的线程不再报告焦点控件，而此时鼠标通常仍在该面板上）→ 整个窗口矩形。
- 结果跳转使用 `doublecmd.exe -C -T -P <L|R> <路径>`：`-T` 让 Double Commander 走 `AddTab()`，结果在新标签页中打开，不会占用用户正在使用的标签页；`-P` 保证新标签出现在呼出搜索的那个面板里。
- Lertaro 的 Hook 进程会在前台窗口每次变化时查询一次活动路径，所以这次临时读取必须完全不可见：查询期间暂停命令行控件的重绘，读到的路径在清空后还会复核一次并重试，避免 Double Commander 稍后写入的文本留在输入框里。`Ctrl+P` 只发送一次（该命令是追加写入），随后最多读三次、每次间隔 25ms，读到路径即返回；仍然读不到时回退到该面板最近一次成功读取的路径（10 秒内有效），而不是返回空值让 Lertaro 丢掉已有搜索范围。同一面板 500ms 内的重复请求直接命中缓存，不会重复注入。
- 进程名只识别 `doublecmd` 与 `doublecmd64`（前者同时用于 32/64 位官方构建）。如果发行版把可执行文件改名（例如 `doublecmd_gui.exe`），插件不会把它当成 Double Commander。

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

## 安装和使用

### 安装 Lertaro 插件

1. 从 GitHub Release 下载 `Lertaro.Plugins.DoubleCommander.dll`，或者下载包含 DLL、Listary 桥接脚本、README 和 LICENSE 的 ZIP。
2. 在 Lertaro **App 根目录**下创建目录：

   ```text
   Plugins\DoubleCommander\
   ```

3. 将 `Lertaro.Plugins.DoubleCommander.dll` 放入该目录。不要把 Release DLL 放入本仓库的 `Lertaro\` 源码目录；源码目录和运行时插件目录不是同一个位置。
4. 启动或重启 Lertaro，在 `Settings -> Plugins` 中确认插件已加载。
5. 启动 Double Commander。将鼠标或键盘焦点放到左、右面板后，在 Lertaro 中搜索文件；打开目录或文件时，结果会回到对应的 Double Commander 面板。

从源码构建并传入 `-p:LertaroRoot=...` 时，项目会自动把 DLL 复制到：

```text
<LertaroRoot>\App\bin\Release\net10.0-windows\Plugins\DoubleCommander\
```

Lertaro 官方开发指南约定第三方插件放在 App 根目录下的 `Plugins\<PluginName>\` 子目录，并在重启后自动扫描；本项目使用的插件目录名是 `DoubleCommander`。

### 使用边界

- 当前实现只适配 Windows 版 Double Commander。
- 路径读取要求 Double Commander 主窗口可见；窗口不在前台、命令行已有输入或路径文本被省略时，插件会放弃该次临时读取。
- Lertaro 插件负责搜索范围、内联搜索和结果跳转；Listary 桥接脚本是独立的自定义命令集成，不需要复制到 Lertaro 的插件目录。

## 自检

路径识别规则不依赖正在运行的 Double Commander，可直接运行：

```powershell
dotnet run --project .\tests\DoubleCommander.Heuristics.Tests.csproj
```

自检覆盖进程名、主窗口类名、文件列表类名（含 LCL 通用 `Window` 类）、路径文本与省略号路径等纯规则。

真正的集成验收仍需要在本机启动 Double Commander，分别测试：普通路径、UNC 路径、中文路径、左右面板切换、标签页、管理员启动、映射盘和超长路径。若 Double Commander 命令行被隐藏、命令行中已有文字，或窗口不在前台，插件会放弃该次局部路径读取，避免打断用户输入。

## 发布

推送 `v*` 标签会自动运行 Windows/.NET 10 构建，并创建 GitHub Release。Release 会附带单独的 DLL，以及包含以下内容的 ZIP：

标签的 `v` 前缀不会写入 DLL；例如 `v0.1.2` 会生成 `FileVersion=0.1.2.0`、`ProductVersion=0.1.2`。发布流程会在打包前校验这两个版本号。

版本号以 **git 标签**为准：构建时会执行 `git describe --tags --match "v*" --abbrev=0` 推导出 `Version`（以及 `AssemblyVersion`/`FileVersion`/`InformationalVersion`），所以 CI 产物、本地构建与发布产物报出的版本一致。发布时 `-p:Version=<标签>` 依然优先（全局属性，覆盖推导值）。`Lertaro/DoubleCommander.csproj` 里的 `<Version>` 只是**回退值**：没有 git、没有标签（源码导出、浅克隆）时才会用到；`Build and test` 工作流会检查它是否落后于最新标签（落后即失败），领先（正在准备下一个版本）则只提示。

```text
Lertaro.Plugins.DoubleCommander.dll
Listary\DoubleCommander.Listary.ps1
README.md
LICENSE
```

```powershell
git tag v0.1.2
git push origin v0.1.2
```

发版前把 `Lertaro/DoubleCommander.csproj` 的 `<Version>` 改成与标签相同的值（Release 工作流会校验两者一致，不一致会直接失败），这样在无 git 环境里构建也能得到正确版本。

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
