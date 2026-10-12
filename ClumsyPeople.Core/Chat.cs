using System.Globalization;
using System.Text;

namespace ClumsyPeople.Core;

public enum ChatChannel { Party, Alliance, Shout, Say, Yell, FreeCompany, Tell }

public static class ChatText
{
    // Conservative command-buffer ceiling used by the client and ECommons.
    // The adapter also checks the live ChatLog input's limit before submitting.
    public const int MaxCommandBytes = 500;

    public static string Prefix(ChatChannel channel, string recipient = "") => channel switch
    {
        ChatChannel.Party => "/p ", ChatChannel.Alliance => "/a ",
        ChatChannel.Shout => "/sh ", ChatChannel.Say => "/s ",
        ChatChannel.Yell => "/y ", ChatChannel.FreeCompany => "/fc ",
        ChatChannel.Tell when ValidRecipient(recipient) => $"/tell {recipient.Trim()} ",
        ChatChannel.Tell => throw new ArgumentException("私聊需要填写角色名@服务器，不允许换行或命令字符。"),
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    public static bool ValidRecipient(string value)
    {
        value = value.Trim();
        var at = value.IndexOf('@');
        return at > 0 && at < value.Length - 1 && at == value.LastIndexOf('@') &&
               value.All(c => char.IsLetterOrDigit(c) || c is ' ' or '\'' or '-' or '@') &&
               !value[(at + 1)..].Contains(' ');
    }

    public static string Clean(string text)
    {
        var b = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune)) { b.Append(' '); continue; }
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse) continue;
            // SeString markers must never enter native chat as payloads.
            if (rune.Value is 2 or 3) continue;
            b.Append(rune);
        }
        return b.ToString().Trim();
    }

    public static IReadOnlyList<string> Split(string text, string prefix, int maxBytes = MaxCommandBytes)
    {
        var budget = maxBytes - Encoding.UTF8.GetByteCount(prefix);
        if (budget < 4) throw new ArgumentException("收件人或频道前缀过长。");
        text = Clean(text);
        var result = new List<string>();
        while (text.Length > 0)
        {
            var bytes = 0;
            var end = 0;
            var sentence = 0;
            var clause = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > budget) break;
                bytes += rune.Utf8SequenceLength;
                end += rune.Utf16SequenceLength;
                if ("。！？!?；;\n".Contains(rune.ToString(), StringComparison.Ordinal)) sentence = end;
                else if ("，,、 ".Contains(rune.ToString(), StringComparison.Ordinal)) clause = end;
            }
            if (end == 0) throw new ArgumentException("聊天长度上限不足。");
            if (end < text.Length) end = sentence > 0 ? sentence : clause > 0 ? clause : end;
            result.Add(prefix + text[..end]);
            text = text[end..];
        }
        return result;
    }

    public static string Correct(string text, IReadOnlyDictionary<string, string> replacements)
    {
        foreach (var pair in replacements.OrderByDescending(p => p.Key.Length))
            if (!string.IsNullOrWhiteSpace(pair.Key)) text = text.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        return Clean(text);
    }
}

public interface IChatTransport
{
    ulong CharacterId { get; }
    string Draft { get; }
    bool CanUse(ChatChannel channel, out string error);
    void Send(string command);
    void Fill(string command);
}

public sealed class DeliveryQueue(IChatTransport transport)
{
    private readonly Queue<string> pending = new();
    private ulong character;
    private ChatChannel channel;
    private DateTimeOffset nextSend;
    public bool Running { get; private set; }
    public string Status { get; private set; } = "就绪";
    public IReadOnlyList<string> Remaining => pending.ToArray();

    public void Prepare(IEnumerable<string> commands, ulong characterId, ChatChannel target, bool automatic, DateTimeOffset now)
    {
        Cancel();
        foreach (var command in commands)
        {
            if (Encoding.UTF8.GetByteCount(command) > ChatText.MaxCommandBytes || command.Contains('\n') || command.Contains('\r') || command.Contains('\0'))
                throw new ArgumentException("聊天内容不合法。");
            pending.Enqueue(command);
        }
        character = characterId;
        channel = target;
        Running = automatic && pending.Count > 0;
        nextSend = now;
        Status = pending.Count == 0 ? "没有可发送的内容" : automatic ? "等待发送" : "等待填入";
    }

    public void Tick(DateTimeOffset now)
    {
        if (Running && now >= nextSend) Deliver(true, now);
    }

    public void FillNext() => Deliver(false, DateTimeOffset.UtcNow);

    private void Deliver(bool send, DateTimeOffset now)
    {
        if (pending.Count == 0) return;
        if (character == 0 || transport.CharacterId != character)
        {
            Running = false;
            Status = "角色已变化，操作已停止，请重新准备内容";
            return;
        }
        if (!transport.CanUse(channel, out var error)) { Running = false; Status = error; return; }
        if (!string.IsNullOrEmpty(transport.Draft)) { Running = false; Status = "聊天框已有草稿，请处理后继续"; return; }
        try
        {
            if (send) transport.Send(pending.Peek()); else transport.Fill(pending.Peek());
            pending.Dequeue();
            nextSend = now.AddSeconds(2);
            if (pending.Count == 0) Running = false;
            Status = send ? $"已提交一段，剩余 {pending.Count} 段（不代表服务器已送达）" : $"已填入一段，剩余 {pending.Count} 段";
        }
        catch (Exception e) { Running = false; Status = $"已停止：{e.Message}"; }
    }

    public void Cancel() { Running = false; pending.Clear(); Status = "已取消"; }
}
