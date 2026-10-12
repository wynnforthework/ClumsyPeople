using Dalamud.Configuration;

namespace ClumsyPeople;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool AutoSend { get; set; }
    public int Microphone { get; set; } = -1;
    public string ModelDirectory { get; set; } = "";
    public string DownloadProxy { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Nicknames { get; set; } = "";
    public Dictionary<string, string> Replacements { get; set; } = new()
    {
        ["艾欧泽亚"] = "艾欧泽亚", ["爱欧泽亚"] = "艾欧泽亚",
        ["利姆萨罗敏萨"] = "利姆萨·罗敏萨", ["以太之光"] = "以太之光",
    };
}
