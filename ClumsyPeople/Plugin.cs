using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using ClumsyPeople.Core;
using ClumsyPeople.Speech;
using System.Collections.Concurrent;

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
    internal SpeechEngine Speech { get; } = new();
    internal string SpeechStatus = "就绪 · 按住频道按钮说话";
    internal bool Processing { get; private set; }
    internal bool Installing { get; private set; }
    internal bool Compact;
    private readonly ConcurrentQueue<Action> completions = new();
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? recognition;
    private int generation;
    private bool disposed;
    private DateTimeOffset recordingStarted;
    private ulong recordingCharacter;
    private ulong lastCharacter;
    private string recordingRecipient = "";
    private string recordingDraft = "";
    private bool draftChanged;
    private bool recordingAuto;
    private string recordingModel = "";
    private Dictionary<string,string> recordingCorrections = [];
    private readonly CompactWindow compactWindow;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands, IFramework framework,
        IPlayerState player, IGameGui gameGui, IPartyList party)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.framework = framework;
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (string.IsNullOrWhiteSpace(Config.ModelDirectory)) Config.ModelDirectory = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "models");
        Chat = new ChatBridge(player, gameGui, party);
        Delivery = new DeliveryQueue(Chat);
        window = new MainWindow(this);
        compactWindow = new CompactWindow(this);
        windows.AddWindow(compactWindow);
        windows.AddWindow(window);
        commands.AddHandler("/clumsy", new CommandInfo((_, _) => window.Toggle()) { HelpMessage = "打开手残党" });
        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi += Open;
        pluginInterface.UiBuilder.OpenConfigUi += Open;
        framework.Update += Update;
    }

    private void Open() => window.IsOpen = true;
    private void Update(IFramework _)
    {
        if (Chat.CharacterId != lastCharacter)
        {
            CancelSpeech(); Delivery.Cancel(); lastCharacter = Chat.CharacterId;
        }
        if (Speech.Recording || Processing) draftChanged |= Chat.Draft != recordingDraft;
        while (completions.TryDequeue(out var action))
            if (!disposed) try { action(); } catch (Exception e) { Processing = false; Error = e.Message; Delivery.Cancel(); }
        if (Speech.Recording && DateTimeOffset.UtcNow - recordingStarted >= TimeSpan.FromSeconds(60)) StopSpeech();
        Delivery.Tick(DateTimeOffset.UtcNow);
    }
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

    internal void BeginSpeech(ChatChannel channel)
    {
        if (Processing || Installing || Speech.Recording) return;
        try
        {
            if (!Chat.CanUse(channel, out var error)) throw new InvalidOperationException(error);
            _ = ChatText.Prefix(channel, Config.Recipient);
            _ = ModelFiles.Find(Config.ModelDirectory);
            Delivery.Cancel();
            SelectedChannel = channel;
            recordingCharacter = Chat.CharacterId;
            recordingRecipient = Config.Recipient;
            recordingDraft = Chat.Draft; draftChanged = false;
            recordingAuto = Config.AutoSend;
            recordingModel = Config.ModelDirectory;
            recordingCorrections = new(Config.Replacements);
            recordingStarted = DateTimeOffset.UtcNow;
            recognition?.Dispose(); recognition = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            generation++;
            Speech.Start(Config.Microphone);
            SpeechStatus = "正在录音 · 松开结束"; Error = "";
        }
        catch (Exception e) { Error = e.Message; }
    }

    internal void StopSpeech()
    {
        if (!Speech.Recording || Processing) return;
        Processing = true; SpeechStatus = "正在识别…";
        var ticket = generation;
        var token = recognition!.Token;
        var character = recordingCharacter;
        var channel = SelectedChannel;
        var recipient = recordingRecipient;
        var auto = recordingAuto;
        var directory = recordingModel;
        var corrections = recordingCorrections;
        _ = ProcessRecording();
        async Task ProcessRecording()
        {
            try
            {
                var samples = await Speech.StopAsync();
                var text = await Task.Run(() => Speech.Recognize(samples, directory, token), token);
                completions.Enqueue(() =>
                {
                    if (ticket != generation || character != Chat.CharacterId || token.IsCancellationRequested) return;
                    Processing = false;
                    Result = ChatText.Correct(text, corrections);
                    SpeechStatus = Result.Length == 0 ? "未识别到语音，请靠近麦克风重试" : "识别完成";
                    if (Result.Length == 0) return;
                    var prefix = ChatText.Prefix(channel, recipient);
                    Delivery.Prepare(ChatText.Split(Result, prefix, Chat.ByteLimit), character, channel,
                        auto && !draftChanged && recordingDraft.Length == 0, DateTimeOffset.UtcNow);
                    if (draftChanged || recordingDraft.Length > 0) Error = "聊天草稿已存在或发生变化，识别结果已保留，请处理草稿后填入。";
                    else if (!auto) Delivery.FillNext();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                completions.Enqueue(() => { if (ticket == generation) { Processing = false; SpeechStatus = "识别失败"; Error = e.Message; } });
            }
        }
    }

    internal void CancelSpeech()
    {
        generation++; recognition?.Cancel(); Speech.Cancel(); Processing = false;
        SpeechStatus = "已取消 · 按住频道按钮重新录音";
    }

    internal void InstallModels()
    {
        if (Installing || Processing || Speech.Recording) return;
        Installing = true;
        var root = Config.ModelDirectory; var proxy = Config.DownloadProxy;
        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<string>(s => completions.Enqueue(() => SpeechStatus = s));
                await ModelInstaller.InstallAsync(root, proxy, progress, lifetime.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { completions.Enqueue(() => Error = "模型安装失败：" + e.Message); }
            finally { completions.Enqueue(() => Installing = false); }
        });
    }

    internal void ShowCompact() { CancelSpeech(); compactWindow.IsOpen = true; window.IsOpen = false; }
    internal void ShowMain() { CancelSpeech(); compactWindow.IsOpen = false; window.IsOpen = true; }

    public void Dispose()
    {
        disposed = true; lifetime.Cancel(); CancelSpeech(); Speech.Dispose(); recognition?.Dispose();
        pluginInterface.UiBuilder.Draw -= windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi -= Open;
        pluginInterface.UiBuilder.OpenConfigUi -= Open;
        commands.RemoveHandler("/clumsy");
        framework.Update -= Update;
        Delivery.Cancel();
        windows.RemoveAllWindows();
    }

}
