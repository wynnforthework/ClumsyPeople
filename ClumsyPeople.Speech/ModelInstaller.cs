using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;

namespace ClumsyPeople.Speech;

public static class ModelInstaller
{
    public const string AsrName = "sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23";
    public const string PunctuationName = "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8";
    public static readonly (string Tag, string Name, string Hash)[] Archives =
    [
        ("asr-models", AsrName, "2cbd71b640d9c37d3784f29367333a4577b0398b62e9deeed418170b081cba8b"),
        ("punctuation-models", PunctuationName, "c0d5aa5f8eeb686032345e180bedf39319dc2e0556781c6264bcadba8328a6e1"),
    ];

    public static async Task InstallAsync(string root, string proxy, IProgress<string> progress, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        using var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(proxy)) handler.Proxy = new WebProxy(new Uri(proxy));
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClumsyPeople/0.1");
        var staging = Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var (tag, name, hash) in Archives)
            {
                token.ThrowIfCancellationRequested();
                var destination = Path.Combine(root, name);
                if (Directory.Exists(destination)) continue;
                progress.Report($"正在下载 {name}（共约 133 MiB）");
                var archive = Path.Combine(staging, name + ".tar.bz2");
                using (var response = await client.GetAsync($"https://github.com/k2-fsa/sherpa-onnx/releases/download/{tag}/{name}.tar.bz2", HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.EnsureSuccessStatusCode();
                    await using var file = File.Create(archive);
                    await response.Content.CopyToAsync(file, token);
                }
                await using (var file = File.OpenRead(archive))
                {
                    var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
                    if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("模型 SHA256 校验失败，未安装");
                }
                progress.Report("校验通过，正在解压模型");
                // Archives are pinned by SHA256. Use the OS bundled bsdtar; no shell command interpolation.
                var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe"))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (var arg in new[] { "-xjf", archive, "-C", staging }) info.ArgumentList.Add(arg);
                using var tar = Process.Start(info) ?? throw new IOException("无法启动 Windows tar.exe");
                var errors = tar.StandardError.ReadToEndAsync(token);
                try { await tar.WaitForExitAsync(token); }
                catch { if (!tar.HasExited) tar.Kill(true); throw; }
                if (tar.ExitCode != 0) throw new IOException("模型解压失败：" + await errors);
                Directory.Move(Path.Combine(staging, name), destination);
            }
            _ = ModelFiles.Find(root);
            progress.Report("模型已就绪，识别无需联网");
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
