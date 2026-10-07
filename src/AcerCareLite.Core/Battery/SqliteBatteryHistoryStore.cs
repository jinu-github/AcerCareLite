using Microsoft.Data.Sqlite;

namespace AcerCareLite.Core.Battery;

/// <summary>Small local history database. One row per sample, keyed by UTC seconds.</summary>
public sealed class SqliteBatteryHistoryStore : IBatteryHistoryStore
{
    private readonly string _connectionString;
    private readonly object _gate = new();
    private bool _initialized;

    public SqliteBatteryHistoryStore(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
    }

    public void Add(BatterySample s)
    {
        Run(cmd =>
        {
            cmd.CommandText = "INSERT OR REPLACE INTO battery_samples (ts, percent, on_ac, charging) VALUES ($ts, $p, $ac, $ch)";
            cmd.Parameters.AddWithValue("$ts", s.Time.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("$p", s.Percent);
            cmd.Parameters.AddWithValue("$ac", s.OnAc ? 1 : 0);
            cmd.Parameters.AddWithValue("$ch", s.Charging ? 1 : 0);
            cmd.ExecuteNonQuery();
        });
    }

    public IReadOnlyList<BatterySample> Query(DateTimeOffset from)
    {
        var list = new List<BatterySample>();
        Run(cmd =>
        {
            cmd.CommandText = "SELECT ts, percent, on_ac, charging FROM battery_samples WHERE ts >= $from ORDER BY ts";
            cmd.Parameters.AddWithValue("$from", from.ToUnixTimeSeconds());
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new BatterySample(DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0)), r.GetInt32(1), r.GetInt32(2) != 0, r.GetInt32(3) != 0));
        });
        return list;
    }

    public void Prune(DateTimeOffset olderThan)
    {
        Run(cmd =>
        {
            cmd.CommandText = "DELETE FROM battery_samples WHERE ts < $t";
            cmd.Parameters.AddWithValue("$t", olderThan.ToUnixTimeSeconds());
            cmd.ExecuteNonQuery();
        });
    }

    private void Run(Action<SqliteCommand> work)
    {
        lock (_gate)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            if (!_initialized)
            {
                using var init = conn.CreateCommand();
                init.CommandText = "CREATE TABLE IF NOT EXISTS battery_samples (ts INTEGER PRIMARY KEY, percent INTEGER NOT NULL, on_ac INTEGER NOT NULL, charging INTEGER NOT NULL)";
                init.ExecuteNonQuery();
                _initialized = true;
            }
            using var cmd = conn.CreateCommand();
            work(cmd);
        }
    }
}
