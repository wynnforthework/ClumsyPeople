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
        ImGui.Separator();
        if (ImGui.BeginTabBar("pages"))
        {
            if (ImGui.BeginTabItem("语音输入")) { Voice(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("留言板")) { DrawBoard(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("设置")) { Settings(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        if (plugin.Error.Length > 0) ImGui.TextWrapped(plugin.Error);
    }

    private void Voice()
    {
        ImGui.TextWrapped("选择频道。识别结果也可以在下方手动编辑。");
        for (var i = 0; i < Labels.Length; i++)
        {
            if (i > 0 && i % 4 != 0) ImGui.SameLine();
            if (ImGui.Button(Labels[i], new Vector2(110, 44))) plugin.SelectedChannel = (ChatChannel)i;
        }
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

    private void DrawBoard() => ImGui.TextWrapped("留言板正在准备。");
    private void Settings() => ImGui.TextWrapped("手残党 · 国服 7.56 / Dalamud API 15。自动发送默认关闭。");
}
