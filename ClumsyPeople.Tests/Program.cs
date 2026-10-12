using System.Text;
using ClumsyPeople.Core;

var passed = 0;
void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { Console.Error.WriteLine($"FAIL {name}: {e}"); Environment.ExitCode = 1; }
}
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
Test("All seven channel prefixes", () => Assert(Enum.GetValues<ChatChannel>().Select(c => ChatText.Prefix(c, "测试玩家@红玉海")).Distinct().Count() == 7));
Test("Tell recipient validation rejects command injection", () =>
{
    foreach (var s in new[] { "", "玩家", "@世界", "玩家@", "玩家@世界\n/logout", "玩家@世界 /s", "玩家@@世界" }) Assert(!ChatText.ValidRecipient(s));
    Assert(ChatText.ValidRecipient("Firstname Lastname@World"));
});
Test("UTF8 boundary includes prefix and recipient", () =>
{
    var prefix = ChatText.Prefix(ChatChannel.Tell, "测试玩家@红玉海");
    var text = string.Concat(Enumerable.Repeat("你好😀", 300));
    var parts = ChatText.Split(text, prefix);
    Assert(parts.Count > 1);
    Assert(parts.All(p => Encoding.UTF8.GetByteCount(p) <= 500));
    Assert(string.Concat(parts.Select(p => p[prefix.Length..])) == text);
    Assert(parts.All(p => !p.Contains('\uFFFD')));
});
Test("Sentence boundaries preferred", () => Assert(ChatText.Split("第一句。第二句非常长非常长非常长。", "/p ", 40)[0] == "/p 第一句。"));
Test("Exact byte limit and one byte overflow", () =>
{
    Assert(ChatText.Split(new string('a', 497), "/p ").Count == 1);
    Assert(ChatText.Split(new string('a', 498), "/p ").Count == 2);
});
Test("Controls removed", () => Assert(ChatText.Clean("你好\n/logout\0\u0002") == "你好 /logout"));
Test("Empty recognition creates no commands", () => Assert(ChatText.Split(" \t\n", "/p ").Count == 0));
Test("Term correction", () => Assert(ChatText.Correct("爱欧泽亚", new Dictionary<string,string> { ["爱欧泽亚"]="艾欧泽亚" }) == "艾欧泽亚"));
Test("Delivery interval and exactly once", () =>
{
    var t = new FakeTransport(); var q = new DeliveryQueue(t); var now = DateTimeOffset.UtcNow;
    q.Prepare(["/p 一", "/p 二"], 1, ChatChannel.Party, true, now);
    q.Tick(now); q.Tick(now); q.Tick(now.AddSeconds(1)); Assert(t.Sent.Count == 1);
    q.Tick(now.AddSeconds(2)); Assert(t.Sent.Count == 2 && !q.Running);
});
Test("Character switch blocks stale results", () =>
{
    var t = new FakeTransport(); var q = new DeliveryQueue(t);
    q.Prepare(["/p 一"], 1, ChatChannel.Party, true, DateTimeOffset.MinValue);
    t.CharacterId = 2; q.Tick(DateTimeOffset.UtcNow); q.FillNext(); Assert(t.Sent.Count == 0 && t.Filled.Count == 0);
});
Test("Existing draft blocks delivery", () =>
{
    var t = new FakeTransport { Draft = "已有内容" }; var q = new DeliveryQueue(t);
    q.Prepare(["/p 一"], 1, ChatChannel.Party, true, DateTimeOffset.MinValue);
    q.Tick(DateTimeOffset.UtcNow); Assert(t.Sent.Count == 0 && q.Remaining.Count == 1 && !q.Running);
});
Test("Fill preserves remaining segments and cancel clears", () =>
{
    var t = new FakeTransport(); var q = new DeliveryQueue(t);
    q.Prepare(["/p 一", "/p 二"], 1, ChatChannel.Party, false, DateTimeOffset.UtcNow);
    q.FillNext(); q.FillNext(); Assert(t.Filled.Count == 1 && q.Remaining.Count == 1);
    q.Cancel(); Assert(q.Remaining.Count == 0);
});
Test("Transport error stops without retry", () =>
{
    var t = new FakeTransport { Fail = true }; var q = new DeliveryQueue(t);
    q.Prepare(["/p 一"], 1, ChatChannel.Party, true, DateTimeOffset.MinValue);
    q.Tick(DateTimeOffset.UtcNow); t.Fail = false; q.Tick(DateTimeOffset.UtcNow.AddSeconds(3));
    Assert(t.Sent.Count == 0 && q.Remaining.Count == 1);
});
Test("Mailbox matching", () =>
{
    Assert(MessageMatcher.Match(13, false, false, "你好", "本人", []) == MessageReason.Tell);
    Assert(MessageMatcher.Match(12, false, false, "本人你好", "本人", []) == null);
    Assert(MessageMatcher.Match(14, true, false, "本人你好", "本人", []) == null);
    Assert(MessageMatcher.Match(14, false, false, "本人你好", "本人", []) == MessageReason.Mention);
    Assert(MessageMatcher.Match(24, false, false, "小名你好", "本人", ["小名"]) == MessageReason.Mention);
    Assert(MessageMatcher.Match(29, false, true, "某人向你挥手", "本人", []) == MessageReason.TargetedEmote);
    Assert(MessageMatcher.Match(29, false, false, "某人向本人挥手", "本人", []) == null);
    Assert(MessageMatcher.Match(41, false, true, "本人受到伤害", "本人", []) == null);
});
Test("SQLite persistence isolation search retention and read status", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "clumsy-test-" + Guid.NewGuid().ToString("N"), "messages.db");
    try
    {
        using (var db = new MessageStore(path))
        {
            for (var i = 0; i < 4; i++) db.Add(new(0, 1, DateTimeOffset.UtcNow, "朋友", "红玉海", "TellIncoming", "你好" + i, MessageReason.Tell), 3);
            db.Add(new(0, 2, DateTimeOffset.UtcNow, "其他角色", "世界", "Party", "隔离", MessageReason.Mention));
            Assert(db.Query(1).Count == 3 && db.Query(2).Count == 1);
            Assert(db.Query(1, "你好3").Count == 1 && db.Query(1, "' OR 1=1 --").Count == 0);
            Assert(db.UnreadCount(1) == 3);
            db.MarkRead(1, db.Query(1)[0].Id); Assert(db.UnreadCount(1) == 2);
        }
        using (var db = new MessageStore(path))
        {
            Assert(db.Query(1).Count == 3 && db.Query(1, unread: true).Count == 2);
            db.Delete(2, db.Query(1)[0].Id); Assert(db.Query(1).Count == 3);
            db.Delete(1, db.Query(1)[0].Id); Assert(db.Query(1).Count == 2);
            db.MarkRead(1); Assert(db.UnreadCount(1) == 0);
        }
    }
    finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
});
Test("Bundled model path and legacy configuration migration", () =>
{
    var legacy = Path.Combine(Path.GetTempPath(), "old-clumsy-models");
    var custom = Path.Combine(Path.GetTempPath(), "custom-clumsy-models");
    var pluginDirectory = Path.Combine(Path.GetTempPath(), "plugin-install");
    var bundled = ClumsyPeople.Speech.ModelFiles.BundledDirectory(pluginDirectory);
    Assert(ClumsyPeople.Speech.ModelFiles.ResolveDirectory("", legacy, pluginDirectory) == bundled);
    Assert(ClumsyPeople.Speech.ModelFiles.ResolveDirectory(legacy, legacy, pluginDirectory) == bundled);
    Assert(ClumsyPeople.Speech.ModelFiles.ResolveDirectory(custom, legacy, pluginDirectory) == custom);
    Assert(bundled == Path.Combine(pluginDirectory, "models"));
});
Test("Model paths work when speech assembly is loaded from memory", () =>
{
    var context = new System.Runtime.Loader.AssemblyLoadContext("clumsy-memory-test", isCollectible: true);
    try
    {
        using var stream = File.OpenRead(typeof(ClumsyPeople.Speech.ModelFiles).Assembly.Location);
        var assembly = context.LoadFromStream(stream);
        Assert(assembly.Location == "");
        var directory = Path.Combine(Path.GetTempPath(), "dalamud-plugin-install");
        var type = assembly.GetType("ClumsyPeople.Speech.ModelFiles")!;
        var result = type.GetMethod("ResolveDirectory")!.Invoke(null, ["", "unused-legacy", directory]);
        Assert((string)result! == Path.Combine(directory, "models"));
    }
    finally { context.Unload(); }
});
if (args.Length > 0)
{
    Test("Native ASR and punctuation inference", () =>
    {
        using var engine = new ClumsyPeople.Speech.SpeechEngine(AppContext.BaseDirectory);
        var root = args[0] == "--bundled" ? ClumsyPeople.Speech.ModelFiles.ResolveDirectory("", "unused-legacy", AppContext.BaseDirectory) : Path.GetFullPath(args[0]);
        var files = ClumsyPeople.Speech.ModelFiles.Find(root);
        Assert(File.Exists(files.Punctuation));
        var wav = Directory.EnumerateFiles(root, "0.wav", SearchOption.AllDirectories).First();
        using var reader = new NAudio.Wave.WaveFileReader(wav);
        var bytes = new byte[reader.Length]; reader.ReadExactly(bytes);
        var samples = Enumerable.Range(0, bytes.Length / 2).Select(i => BitConverter.ToInt16(bytes, i * 2) / 32768f).ToArray();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var text = engine.Recognize(samples, root, CancellationToken.None);
        Console.WriteLine($"ASR: {text} ({clock.ElapsedMilliseconds} ms, sample rate {reader.WaveFormat.SampleRate})");
        Assert(text.Length > 0);
        Assert(engine.Recognize(new float[16000], root, CancellationToken.None) == "");
    });
}
Console.WriteLine($"{passed} tests passed");

sealed class FakeTransport : IChatTransport
{
    public ulong CharacterId { get; set; } = 1;
    public string Draft { get; set; } = "";
    public bool Fail { get; set; }
    public List<string> Sent { get; } = [];
    public List<string> Filled { get; } = [];
    public bool CanUse(ChatChannel channel, out string error) { error = ""; return true; }
    public void Send(string command) { if (Fail) throw new Exception("模拟提交失败"); Sent.Add(command); }
    public void Fill(string command) { Filled.Add(command); Draft = command; }
}
