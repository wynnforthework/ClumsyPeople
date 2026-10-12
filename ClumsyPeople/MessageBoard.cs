using System.Collections.Concurrent;
using System.Text.Json;
using ClumsyPeople.Core;

namespace ClumsyPeople;

internal sealed class MessageBoard : IDisposable
{
    private readonly BlockingCollection<Action> jobs = new(1024);
    private readonly Task worker;
    private readonly MessageStore store;
    private readonly Action<Exception> reportError;
    private int dropped;
    public int Dropped => Volatile.Read(ref dropped);
    public MessageBoard(string directory, Action<Exception> reportError)
    {
        this.reportError = reportError;
        store = new MessageStore(Path.Combine(directory, "messages.db"));
        worker = Task.Run(() =>
        {
            foreach (var action in jobs.GetConsumingEnumerable())
                try { action(); } catch (Exception e) { reportError(e); }
            store.Dispose();
        });
    }
    public void Enqueue(MessageRecord record) => AddJob(() => store.Add(record));
    public void Read(ulong character, string search, MessageReason? reason, bool unread, int page, Action<IReadOnlyList<MessageRecord>, int> done)
        => AddJob(() => done(store.Query(character, search, reason, unread, page * 200), store.UnreadCount(character)));
    public void MarkRead(ulong character, long? id) => AddJob(() => store.MarkRead(character, id));
    public void Delete(ulong character, long id) => AddJob(() => store.Delete(character, id));
    public void Export(ulong character, string path, Action done) => AddJob(() =>
    {
        File.WriteAllText(path, JsonSerializer.Serialize(store.Query(character, limit: 10000), new JsonSerializerOptions { WriteIndented = true }));
        done();
    });
    private void AddJob(Action action)
    {
        if (jobs.IsAddingCompleted) return;
        if (!jobs.TryAdd(action)) { Interlocked.Increment(ref dropped); reportError(new IOException("留言后台队列已满，部分操作未保存")); }
    }
    public void Dispose()
    {
        jobs.CompleteAdding();
        // Drain pending writes before returning; bounded queue prevents unbounded shutdown.
        worker.GetAwaiter().GetResult(); jobs.Dispose();
    }
}
