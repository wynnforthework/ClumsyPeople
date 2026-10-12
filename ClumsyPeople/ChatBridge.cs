using System.Text;
using ClumsyPeople.Core;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace ClumsyPeople;

internal sealed unsafe class ChatBridge(IPlayerState player, IGameGui gui, IPartyList party) : IChatTransport
{
    public ulong CharacterId => player.ContentId;
    private AddonChatLog* Chat => (AddonChatLog*)gui.GetAddonByName("ChatLog").Address;
    public string Draft => Chat != null && Chat->TextInput != null ? Chat->TextInput->EvaluatedString.ToString() : "";

    public bool CanUse(ChatChannel channel, out string error)
    {
        error = "";
        if (CharacterId == 0) error = "请先登录角色";
        else if (Chat == null || Chat->TextInput == null) error = "游戏聊天框不可用";
        else if (channel is ChatChannel.Party or ChatChannel.Alliance && party.Length == 0) error = "当前不在小队中";
        return error.Length == 0;
    }

    public int ByteLimit
    {
        get
        {
            if (Chat == null || Chat->TextInput == null) return ChatText.MaxCommandBytes;
            var limit = Chat->TextInput->HandlerValues.MaxByte.Int;
            return limit > 4 ? Math.Min(limit, ChatText.MaxCommandBytes) : ChatText.MaxCommandBytes;
        }
    }

    private void Validate(string command)
    {
        if (!command.StartsWith('/') || Encoding.UTF8.GetByteCount(command) > ByteLimit) throw new ArgumentException("内容超过当前聊天框的字节限制，请重新分段");
        using var text = new NativeText(command);
        text.Value->SanitizeString((AllowedEntities)0x27F);
        if (text.Value->ToString() != command) throw new ArgumentException("包含游戏不允许的字符");
    }

    public void Fill(string command)
    {
        Validate(command);
        Chat->TextInput->SetText(command);
        if (Draft != command) throw new InvalidOperationException("聊天框未完整接收文本，结果仍保留在插件内");
    }

    public void Send(string command)
    {
        Validate(command);
        var module = UIModule.Instance();
        if (module == null) throw new InvalidOperationException("游戏 UI 不可用");
        using var text = new NativeText(command);
        module->ProcessChatBoxEntry(text.Value);
    }

    private sealed class NativeText(string text) : IDisposable
    {
        public Utf8String* Value { get; } = Utf8String.FromString(text);
        public void Dispose() => Value->Dtor(true);
    }
}
