using NAudio.Wave;
using SherpaOnnx;
using System.Runtime.InteropServices;

namespace ClumsyPeople.Speech;

public sealed class SpeechEngine : IDisposable
{
    private readonly object gate = new();
    private readonly string pluginDirectory;

    public SpeechEngine(string pluginDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
        this.pluginDirectory = Path.GetFullPath(pluginDirectory);
    }
    private OnlineRecognizer? recognizer;
    private OfflinePunctuation? punctuation;
    private string loadedDirectory = "";
    private WaveInEvent? microphone;
    private List<float> audio = [];
    private TaskCompletionSource<float[]>? stopped;
    private bool cancelled;
    private static readonly object nativeGate = new();
    private static bool nativeLoaded;
    public float Level { get; private set; }
    public bool Recording => microphone != null;
    public static IReadOnlyList<string> Devices => Enumerable.Range(0, WaveInEvent.DeviceCount).Select(i => WaveInEvent.GetCapabilities(i).ProductName).ToArray();

    public void Start(int device)
    {
        if (microphone != null) throw new InvalidOperationException("已经在录音");
        if (WaveInEvent.DeviceCount == 0) throw new InvalidOperationException("未找到麦克风，请检查 Windows 麦克风权限");
        audio = []; cancelled = false; Level = 0;
        stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionAudio = audio;
        var sessionStopped = stopped;
        var capture = new WaveInEvent { DeviceNumber = device, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 40 };
        capture.DataAvailable += (_, e) =>
        {
            lock (sessionAudio)
            {
                if (cancelled) return;
                float peak = 0;
                for (var i = 0; i + 1 < e.BytesRecorded && sessionAudio.Count < 16000 * 60; i += 2)
                {
                    var sample = BitConverter.ToInt16(e.Buffer, i) / 32768f;
                    sessionAudio.Add(sample); peak = Math.Max(peak, Math.Abs(sample));
                }
                Level = peak;
            }
        };
        capture.RecordingStopped += (_, e) =>
        {
            lock (sessionAudio)
            {
                if (e.Exception != null) sessionStopped.TrySetException(e.Exception);
                else sessionStopped.TrySetResult(cancelled ? [] : sessionAudio.ToArray());
            }
        };
        microphone = capture;
        try { capture.StartRecording(); }
        catch { microphone = null; capture.Dispose(); throw; }
    }

    public async Task<float[]> StopAsync()
    {
        var capture = microphone;
        if (capture == null || stopped == null) return [];
        var task = stopped.Task;
        microphone = null;
        try
        {
            capture.StopRecording();
            return await task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        finally { capture.Dispose(); Level = 0; }
    }

    public void Cancel()
    {
        lock (audio) cancelled = true;
        stopped?.TrySetResult([]);
        var capture = microphone;
        microphone = null;
        if (capture != null) { capture.StopRecording(); capture.Dispose(); }
        Level = 0;
    }

    public string Recognize(float[] samples, string directory, CancellationToken cancellation)
    {
        lock (gate)
        {
            cancellation.ThrowIfCancellationRequested();
            if (samples.Length < 1600 || samples.All(s => Math.Abs(s) < .002f)) return "";
            EnsureNativeLibraries();
            if (recognizer == null || loadedDirectory != directory)
            {
                recognizer?.Dispose(); punctuation?.Dispose(); recognizer = null; punctuation = null;
                var files = ModelFiles.Find(directory);
                var config = new OnlineRecognizerConfig();
                config.FeatConfig.SampleRate = 16000;
                config.FeatConfig.FeatureDim = 80;
                config.ModelConfig.Transducer.Encoder = files.Encoder;
                config.ModelConfig.Transducer.Decoder = files.Decoder;
                config.ModelConfig.Transducer.Joiner = files.Joiner;
                config.ModelConfig.Tokens = files.Tokens;
                config.ModelConfig.NumThreads = 2;
                config.ModelConfig.Provider = "cpu";
                config.DecodingMethod = "greedy_search";
                recognizer = new OnlineRecognizer(config);
                var pc = new OfflinePunctuationConfig();
                pc.Model.CtTransformer = files.Punctuation;
                pc.Model.NumThreads = 1;
                punctuation = new OfflinePunctuation(pc);
                loadedDirectory = directory;
            }
            using var stream = recognizer.CreateStream();
            stream.AcceptWaveform(16000, samples);
            stream.AcceptWaveform(16000, new float[16000]);
            stream.InputFinished();
            while (recognizer.IsReady(stream)) { cancellation.ThrowIfCancellationRequested(); recognizer.Decode(stream); }
            var text = recognizer.GetResult(stream).Text.Trim();
            cancellation.ThrowIfCancellationRequested();
            return text.Length == 0 ? "" : punctuation!.AddPunct(text);
        }
    }

    public void Dispose()
    {
        Cancel();
        lock (gate) { recognizer?.Dispose(); punctuation?.Dispose(); recognizer = null; punctuation = null; }
    }

    private void EnsureNativeLibraries()
    {
        lock (nativeGate)
        {
            if (nativeLoaded) return;
            var root = pluginDirectory;
            var directory = File.Exists(Path.Combine(root, "onnxruntime.dll")) ? root : Path.Combine(root, "runtimes", "win-x64", "native");
            // Load the pinned dependency from the plugin package before loading its C API.
            _ = NativeLibrary.Load(Path.Combine(directory, "onnxruntime.dll"));
            var handle = NativeLibrary.Load(Path.Combine(directory, "sherpa-onnx-c-api.dll"));
            NativeLibrary.SetDllImportResolver(typeof(OnlineRecognizer).Assembly, (name, _, _) => name.Contains("sherpa-onnx-c-api", StringComparison.Ordinal) ? handle : IntPtr.Zero);
            nativeLoaded = true;
        }
    }
}

public sealed record ModelFiles(string Encoder, string Decoder, string Joiner, string Tokens, string Punctuation)
{
    public static string BundledDirectory(string pluginDirectory) => Path.Combine(pluginDirectory, "models");

    public static string ResolveDirectory(string configured, string legacyDefault, string pluginDirectory)
    {
        if (string.IsNullOrWhiteSpace(configured) ||
            Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Equals(Path.GetFullPath(legacyDefault).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return BundledDirectory(pluginDirectory);
        return configured;
    }

    public static ModelFiles Find(string directory)
    {
        if (!Directory.Exists(directory)) throw new FileNotFoundException("模型目录不存在，请完整解压插件安装包，或在设置中选择随附模型");
        string FindFile(string pattern) => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
            .OrderBy(p => p.Length).FirstOrDefault() ?? throw new FileNotFoundException($"模型缺少 {pattern}");
        return new(FindFile("encoder*.int8.onnx"), FindFile("decoder*.int8.onnx"), FindFile("joiner*.int8.onnx"), FindFile("tokens.txt"), FindFile("model.int8.onnx"));
    }
}

