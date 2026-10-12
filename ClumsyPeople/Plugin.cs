using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using ClumsyPeople.Core;

namespace ClumsyPeople;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly WindowSystem windows = new("ClumsyPeople");
    private readonly MainWindow window;
    private readonly IFramework framework;
    internal Configuration Config { get; }
    internal ChatBridge Chat { get; }
    internal DeliveryQueue Delivery { get; }
    internal string Result = "";
    internal string Error = "";
    internal ChatChannel SelectedChannel;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands, IFramework framework,
        IPlayerState player, IGameGui gameGui, IPartyList party)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.framework = framework;
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Chat = new ChatBridge(player, gameGui, party);
        Delivery = new DeliveryQueue(Chat);
        window = new MainWindow(this);
        windows.AddWindow(window);
        commands.AddHandler("/clumsy", new CommandInfo((_, _) => window.Toggle()) { HelpMessage = "打开手残党" });
        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi += Open;
        pluginInterface.UiBuilder.OpenConfigUi += Open;
        framework.Update += Update;
    }

    private void Open() => window.IsOpen = true;
    private void Update(IFramework _) => Delivery.Tick(DateTimeOffset.UtcNow);
    internal void Save() => pluginInterface.SavePluginConfig(Config);

    internal void Prepare(bool automatic)
    {
        try
        {
            var prefix = ChatText.Prefix(SelectedChannel, Config.Recipient);
            Delivery.Prepare(ChatText.Split(Result, prefix, Chat.ByteLimit), Chat.CharacterId,
                SelectedChannel, automatic, DateTimeOffset.UtcNow);
            if (!automatic) Delivery.FillNext();
            Error = "";
        }
        catch (Exception e) { Error = e.Message; }
    }

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi -= Open;
        pluginInterface.UiBuilder.OpenConfigUi -= Open;
        commands.RemoveHandler("/clumsy");
        framework.Update -= Update;
        Delivery.Cancel();
        windows.RemoveAllWindows();
    }

}
