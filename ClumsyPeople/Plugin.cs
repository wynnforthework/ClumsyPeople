using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace ClumsyPeople;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly WindowSystem windows = new("ClumsyPeople");
    private readonly WelcomeWindow window = new();

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        windows.AddWindow(window);
        commands.AddHandler("/clumsy", new CommandInfo((_, _) => window.Toggle()) { HelpMessage = "打开手残党" });
        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi += Open;
        pluginInterface.UiBuilder.OpenConfigUi += Open;
    }

    private void Open() => window.IsOpen = true;

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi -= Open;
        pluginInterface.UiBuilder.OpenConfigUi -= Open;
        commands.RemoveHandler("/clumsy");
        windows.RemoveAllWindows();
    }

    private sealed class WelcomeWindow() : Window("手残党###ClumsyPeople")
    {
        public override void Draw() => ImGui.TextUnformatted("手残党 · 离线语音与留言板");
    }
}
