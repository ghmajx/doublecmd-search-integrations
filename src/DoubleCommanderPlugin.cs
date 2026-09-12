using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.DoubleCommander;

public sealed class DoubleCommanderPlugin : IPlugin
{
    public string Name => "Double Commander";

    public string Description => "Double Commander 双栏文件管理器的活动路径、内联搜索和快速导航适配。";

    public string? WebsiteUrl => "https://doublecmd.github.io/";

    public string? WebsiteLabel => "Double Commander";
}
