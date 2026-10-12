using System.Numerics;
using System.Text;
using ClumsyPeople.Core;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace ClumsyPeople;

internal sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private string termFrom = "";
    private string termTo = "";
    private long deleting;
    private static readonly string[] Labels = ["小队", "团队", "喊话", "说话", "呼喊", "部队", "私聊"];
    public MainWindow(Plugin plugin) : base("手残党###ClumsyPeople")
    {
        this.plugin = plugin;
        Size = new Vector2(760, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(560, 380), MaximumSize = new Vector2(float.MaxValue) };
    }

    public override void Draw()
    {
        using var colors = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(.08f,.09f,.12f,1))
            .Push(ImGuiCol.Button, new Vector4(.36f,.24f,.60f,1))
            .Push(ImGuiCol.ButtonHovered, new Vector4(.52f,.35f,.78f,1))
            .Push(ImGuiCol.ButtonActive, new Vector4(.65f,.43f,.95f,1));
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 7f).Push(ImGuiStyleVar.ChildRounding, 10f);
        ImGui.TextColored(new Vector4(.78f,.60f,1,1), "手残党");
        ImGui.SameLine(); ImGui.TextDisabled("离线语音 · 重要留言");
        ImGui.SameLine(); if (ImGui.SmallButton("紧凑模式")) plugin.ShowCompact();
        ImGui.Separator();
        if (ImGui.BeginTabBar("pages"))
        {
            if (ImGui.BeginTabItem("语音输入")) { Voice(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem($"留言板 ({plugin.Unread})###board")) { DrawBoard(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("设置")) { Settings(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        if (plugin.Error.Length > 0) ImGui.TextWrapped(plugin.Error);
    }

    private void Voice()
    {
        ChannelButtons(plugin);
        ImGui.TextUnformatted($"当前频道：{Labels[(int)plugin.SelectedChannel]}");
        if (plugin.SelectedChannel == ChatChannel.Tell)
        {
            var recipient = plugin.Config.Recipient;
            if (ImGui.InputText("角色名@服务器", ref recipient, 128)) { plugin.Config.Recipient = recipient; plugin.Save(); }
        }
        var auto = plugin.Config.AutoSend;
        if (ImGui.Checkbox("识别后直接发送（超长分段，间隔 2 秒）", ref auto)) { plugin.Config.AutoSend = auto; plugin.Save(); }
        ImGui.InputTextMultiline("##result", ref plugin.Result, 32768, new Vector2(-1, 160));
        ImGui.TextDisabled($"正文 {Encoding.UTF8.GetByteCount(plugin.Result)} 字节 · 每段含命令最多 {plugin.Chat.ByteLimit} 字节");
        if (ImGui.Button("填入 / 重新分段")) plugin.Prepare(false);
        ImGui.SameLine(); if (ImGui.Button("发送 / 重新分段")) plugin.Prepare(true);
        ImGui.SameLine(); if (ImGui.Button("复制全文")) ImGui.SetClipboardText(plugin.Result);
        if (ImGui.Button("填入下一段")) plugin.Delivery.FillNext();
        ImGui.SameLine(); if (ImGui.Button("取消发送队列")) plugin.Delivery.Cancel();
        ImGui.TextWrapped(plugin.Delivery.Status);
        using var preview = ImRaii.Child("segments", new Vector2(0, 110), true);
        foreach (var part in plugin.Delivery.Remaining) ImGui.TextWrapped($"[{Encoding.UTF8.GetByteCount(part)} 字节] {part}");
    }

    private void DrawBoard()
    {
        ImGui.TextWrapped("保存插件在线期间收到的私聊、提及和定向动作。角色离线期间未收到的消息无法补取。");
        if (ImGui.InputText("搜索正文 / 发送者", ref plugin.Search, 512)) { plugin.Page = 0; plugin.RefreshBoard(); }
        if (ImGui.Combo("类型", ref plugin.ReasonFilter, "全部\0私聊\0提及\0定向动作\0")) { plugin.Page = 0; plugin.RefreshBoard(); }
        if (ImGui.Checkbox("仅未读", ref plugin.OnlyUnread)) { plugin.Page = 0; plugin.RefreshBoard(); }
        if (ImGui.Button("刷新")) plugin.RefreshBoard();
        ImGui.SameLine(); if (ImGui.Button("全部标为已读")) { plugin.Board.MarkRead(plugin.Chat.CharacterId, null); plugin.RefreshBoard(); }
        ImGui.SameLine(); if (ImGui.Button("导出该角色全部留言")) plugin.ExportBoard();
        ImGui.TextDisabled($"未读 {plugin.Unread} · 第 {plugin.Page + 1} 页，每页 200 条 · 后台队列丢弃 {plugin.Board.Dropped}");
        if (ImGui.Button("上一页") && plugin.Page > 0) { plugin.Page--; plugin.RefreshBoard(); }
        ImGui.SameLine(); if (ImGui.Button("下一页") && plugin.Messages.Count == 200) { plugin.Page++; plugin.RefreshBoard(); }
        using var child = ImRaii.Child("messages", new Vector2(0, -1), true);
        foreach (var item in plugin.Messages)
        {
            ImGui.PushID(item.Id.ToString());
            ImGui.TextColored(item.Read ? new Vector4(.6f,.6f,.65f,1) : new Vector4(.78f,.6f,1,1),
                $"{item.Time.LocalDateTime:MM-dd HH:mm:ss} · {item.Sender}@{item.World} · {item.Channel} · {item.Reason}");
            ImGui.TextWrapped(item.Text);
            if (ImGui.SmallButton("已读")) { plugin.Board.MarkRead(item.CharacterId, item.Id); plugin.RefreshBoard(); }
            ImGui.SameLine(); if (ImGui.SmallButton("复制")) ImGui.SetClipboardText(item.Text);
            ImGui.SameLine(); if (ImGui.SmallButton(deleting == item.Id ? "确认删除" : "删除"))
            {
                if (deleting == item.Id) { plugin.Board.Delete(item.CharacterId, item.Id); plugin.RefreshBoard(); deleting = 0; }
                else deleting = item.Id;
            }
            ImGui.Separator(); ImGui.PopID();
        }
    }
    internal static void ChannelButtons(Plugin plugin)
    {
        ImGui.TextWrapped(plugin.SpeechStatus);
        ImGui.ProgressBar(plugin.Speech.Level, new Vector2(-1, 8), "");
        for (var i = 0; i < Labels.Length; i++)
        {
            if (i > 0 && i % 4 != 0) ImGui.SameLine();
            ImGui.Button(Labels[i], new Vector2(100, 36));
            if (ImGui.IsItemActivated()) plugin.BeginSpeech((ChatChannel)i);
        }
        if (plugin.Speech.Recording && !ImGui.IsMouseDown(ImGuiMouseButton.Left)) plugin.StopSpeech();
        if (plugin.Speech.Recording && !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) plugin.CancelSpeech();
        if (ImGui.Button("取消录音 / 识别")) plugin.CancelSpeech();
    }

    public override void OnClose() => plugin.CancelSpeech();

    private void Settings()
    {
        ImGui.TextWrapped("国服 7.56 / Dalamud API 15 · 中文离线识别 · 首次安装约 133 MiB");
        var devices = Speech.SpeechEngine.Devices;
        var mic = plugin.Config.Microphone;
        if (ImGui.BeginCombo("麦克风", mic < 0 || mic >= devices.Count ? "系统默认" : devices[mic]))
        {
            if (ImGui.Selectable("系统默认", mic == -1)) { plugin.Config.Microphone = -1; plugin.Save(); }
            for (var i = 0; i < devices.Count; i++)
                if (ImGui.Selectable(devices[i] + "##" + i, mic == i)) { plugin.Config.Microphone = i; plugin.Save(); }
            ImGui.EndCombo();
        }
        var path = plugin.Config.ModelDirectory;
        if (ImGui.InputText("模型根目录", ref path, 1024)) { plugin.Config.ModelDirectory = path; plugin.Save(); }
        ImGui.TextWrapped("导入：将两个官方模型目录放在同一根目录，再设置上方路径。");
        var proxy = plugin.Config.DownloadProxy;
        if (ImGui.InputText("下载代理（可留空）", ref proxy, 256)) { plugin.Config.DownloadProxy = proxy; plugin.Save(); }
        using (ImRaii.Disabled(plugin.Installing || plugin.Processing || plugin.Speech.Recording))
            if (ImGui.Button("下载并校验模型")) plugin.InstallModels();
        if (ImGui.Button("检查本地模型"))
        {
            try { _ = Speech.ModelFiles.Find(path); plugin.Error = "模型文件齐全"; }
            catch (Exception e) { plugin.Error = e.Message; }
        }
        ImGui.Separator();
        var recipient = plugin.Config.Recipient;
        if (ImGui.InputText("私聊收件人（角色名@服务器）", ref recipient, 128)) { plugin.Config.Recipient = recipient; plugin.Save(); }
        var nicknames = plugin.Config.Nicknames;
        if (ImGui.InputText("留言昵称（逗号分隔）", ref nicknames, 1024)) { plugin.Config.Nicknames = nicknames; plugin.Save(); }
        ImGui.Separator();
        ImGui.TextUnformatted("识别词汇纠正");
        ImGui.InputText("原词", ref termFrom, 128); ImGui.InputText("替换为", ref termTo, 128);
        if (ImGui.Button("添加 / 更新") && !string.IsNullOrWhiteSpace(termFrom)) { plugin.Config.Replacements[termFrom] = termTo; plugin.Save(); }
        foreach (var pair in plugin.Config.Replacements.ToArray())
        {
            ImGui.TextUnformatted($"{pair.Key} → {pair.Value}"); ImGui.SameLine();
            if (ImGui.SmallButton("删除##" + pair.Key)) { plugin.Config.Replacements.Remove(pair.Key); plugin.Save(); }
        }
    }
}

internal sealed class CompactWindow(Plugin plugin) : Window("手残党 · 按住说话###ClumsyPeopleCompact")
{
    public override void Draw()
    {
        MainWindow.ChannelButtons(plugin);
        ImGui.TextWrapped(plugin.Delivery.Status);
        if (ImGui.Button("打开主窗口")) plugin.ShowMain();
        ImGui.SameLine(); if (ImGui.Button("停止发送")) plugin.Delivery.Cancel();
        if (plugin.Error.Length > 0) ImGui.TextWrapped(plugin.Error);
    }
    public override void OnClose() => plugin.CancelSpeech();
}
