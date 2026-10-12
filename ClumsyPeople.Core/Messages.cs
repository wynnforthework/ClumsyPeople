using Microsoft.Data.Sqlite;

namespace ClumsyPeople.Core;

public enum MessageReason { Tell, Mention, TargetedEmote }
public sealed record MessageRecord(long Id, ulong CharacterId, DateTimeOffset Time, string Sender,
    string World, string Channel, string Text, MessageReason Reason, bool Read = false);

public static class MessageMatcher
{
    public static MessageReason? Match(int kind, bool fromSelf, bool targetsSelf, string text, string ownName, IEnumerable<string> nicknames)
    {
        if (fromSelf || string.IsNullOrWhiteSpace(text)) return null;
        if (kind == 12) return null; // outgoing tell even when relationship metadata is absent
        if (kind is 13 or 80) return MessageReason.Tell; // received tell / GM tell
        if (kind is 28 or 29) return targetsSelf ? MessageReason.TargetedEmote : null;
        // Player conversation channels only; never combat, echo, NPC or system messages.
        if (!(kind is >= 10 and <= 27 or 30 or 32 or 36 or 37 or >= 81 and <= 94 or >= 101 and <= 107)) return null;
        if (!string.IsNullOrWhiteSpace(ownName) && text.Contains(ownName, StringComparison.OrdinalIgnoreCase)) return MessageReason.Mention;
        return nicknames.Any(n => !string.IsNullOrWhiteSpace(n) && text.Contains(n.Trim(), StringComparison.OrdinalIgnoreCase)) ? MessageReason.Mention : null;
    }
}

public sealed class MessageStore : IDisposable
{
    private readonly object gate = new();
    private readonly SqliteConnection db;
    public MessageStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT, character TEXT NOT NULL, time TEXT NOT NULL,
                sender TEXT NOT NULL, world TEXT NOT NULL, channel TEXT NOT NULL,
                text TEXT NOT NULL, reason INTEGER NOT NULL, read INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS ix_messages_character ON messages(character,id);
            PRAGMA user_version=1;
            """;
        cmd.ExecuteNonQuery();
    }

    public void Add(MessageRecord record, int retention = 10000)
    {
        lock (gate)
        {
            using var tx = db.BeginTransaction();
            using var cmd = db.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO messages(character,time,sender,world,channel,text,reason) VALUES($c,$t,$s,$w,$h,$m,$r)";
            cmd.Parameters.AddWithValue("$c", record.CharacterId.ToString());
            cmd.Parameters.AddWithValue("$t", record.Time.ToString("O"));
            cmd.Parameters.AddWithValue("$s", record.Sender); cmd.Parameters.AddWithValue("$w", record.World);
            cmd.Parameters.AddWithValue("$h", record.Channel); cmd.Parameters.AddWithValue("$m", record.Text);
            cmd.Parameters.AddWithValue("$r", (int)record.Reason); cmd.ExecuteNonQuery();
            cmd.Parameters.Clear();
            cmd.CommandText = "DELETE FROM messages WHERE character=$c AND id NOT IN (SELECT id FROM messages WHERE character=$c ORDER BY id DESC LIMIT $n)";
            cmd.Parameters.AddWithValue("$c", record.CharacterId.ToString()); cmd.Parameters.AddWithValue("$n", Math.Max(1, retention));
            cmd.ExecuteNonQuery(); tx.Commit();
        }
    }

    public IReadOnlyList<MessageRecord> Query(ulong character, string search = "", MessageReason? reason = null, bool unread = false, int offset = 0, int limit = 200)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                SELECT id,time,sender,world,channel,text,reason,read FROM messages WHERE character=$c
                AND ($q='' OR instr(lower(text),lower($q))>0 OR instr(lower(sender),lower($q))>0)
                AND ($r=-1 OR reason=$r) AND ($u=0 OR read=0) ORDER BY id DESC LIMIT $n OFFSET $o
                """;
            cmd.Parameters.AddWithValue("$c", character.ToString()); cmd.Parameters.AddWithValue("$q", search);
            cmd.Parameters.AddWithValue("$r", reason.HasValue ? (int)reason.Value : -1); cmd.Parameters.AddWithValue("$u", unread ? 1 : 0);
            cmd.Parameters.AddWithValue("$n", Math.Clamp(limit, 1, 10000)); cmd.Parameters.AddWithValue("$o", Math.Max(0, offset));
            using var reader = cmd.ExecuteReader(); var results = new List<MessageRecord>();
            while (reader.Read()) results.Add(new(reader.GetInt64(0), character, DateTimeOffset.Parse(reader.GetString(1)),
                reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), (MessageReason)reader.GetInt32(6), reader.GetInt32(7) != 0));
            return results;
        }
    }

    public int UnreadCount(ulong character)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM messages WHERE character=$c AND read=0";
            cmd.Parameters.AddWithValue("$c", character.ToString()); return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    public void MarkRead(ulong character, long? id = null) => Mutate("UPDATE messages SET read=1 WHERE character=$c" + (id.HasValue ? " AND id=$id" : ""), character, id);
    public void Delete(ulong character, long id) => Mutate("DELETE FROM messages WHERE character=$c AND id=$id", character, id);
    private void Mutate(string sql, ulong character, long? id)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = sql; cmd.Parameters.AddWithValue("$c", character.ToString());
            if (id.HasValue) cmd.Parameters.AddWithValue("$id", id.Value); cmd.ExecuteNonQuery();
        }
    }
    public void Dispose() { lock (gate) db.Dispose(); }
}
