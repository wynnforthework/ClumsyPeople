using NAudio.Wave;
using SherpaOnnx;

namespace ClumsyPeople.Speech;

public sealed class SpeechEngine : IDisposable
{
    private readonly object gate = new();
    private OnlineRecognizer? recognizer;
    private OfflinePunctuation? punctuation;
    private string loadedDirectory = "";
    private WaveInEvent? microphone;
    private List<float> audio = [];
    private TaskCompletionSource<float[]>? stopped;
    private bool cancelled;
    public float Level { get; private set; }
    public bool Recording => microphone != null;
    public static IReadOnlyList<string> Devices => Enumerable.Range(0, WaveInEvent.DeviceCount).Select(i => WaveInEvent.GetCapabilities(i).ProductName).ToArray();

    public void Start(int device)
    {
        if (microphone != null) throw new InvalidOperationException("Already recording");
        if (WaveInEvent.DeviceCount == 0) throw new InvalidOperationException("No microphone available. Check Windows permissions.");
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
}

public sealed record ModelFiles(string Encoder, string Decoder, string Joiner, string Tokens, string Punctuation)
{
    public static ModelFiles Find(string directory)
    {
        if (!Directory.Exists(directory)) throw new FileNotFoundException("Install or import speech models in Settings first.");
        string FindFile(string pattern) => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
            .OrderBy(p => p.Length).FirstOrDefault() ?? throw new FileNotFoundException($"Missing model {pattern}");
        return new(FindFile("encoder*.int8.onnx"), FindFile("decoder*.int8.onnx"), FindFile("joiner*.int8.onnx"), FindFile("tokens.txt"), FindFile("model.int8.onnx"));
    }
}

