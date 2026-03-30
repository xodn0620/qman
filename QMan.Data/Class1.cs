using Microsoft.Data.Sqlite;
using QMan.Core;

namespace QMan.Data;

/// <summary>
/// SQLite 연결 및 마이그레이션/vec 확장 로딩을 담당하는 래퍼 (Java Db 대응).
/// </summary>
public sealed class SqliteDb : IDisposable
{
    public AppConfig Config { get; }
    public SqliteConnection Connection { get; }
    public bool VecEnabled { get; private set; }

    public SqliteDb(AppConfig config)
    {
        Config = config;
        AppPaths.EnsureDirs();

        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = AppPaths.DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };

        Connection = new SqliteConnection(csb.ConnectionString);
        Connection.Open();

        using (var cmd = Connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = ON;";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "PRAGMA journal_mode = WAL;";
            cmd.ExecuteNonQuery();
        }

        VecEnabled = TryLoadSqliteVec(Connection, config.SqliteVecDllPath);
        Migrate();
    }

    private static bool TryLoadSqliteVec(SqliteConnection conn, string? dllPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dllPath)) return false;
            if (!File.Exists(dllPath)) return false;

            conn.EnableExtensions(true);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT load_extension($path);";
            cmd.Parameters.AddWithValue("$path", dllPath);
            cmd.ExecuteNonQuery();

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Migrate()
    {
        using var cmd = Connection.CreateCommand();

        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS app_kv (
              key   TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS categories (
              id         INTEGER PRIMARY KEY AUTOINCREMENT,
              name       TEXT NOT NULL UNIQUE,
              created_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS documents (
              id           INTEGER PRIMARY KEY AUTOINCREMENT,
              category_id  INTEGER NULL,
              original_name TEXT NOT NULL,
              stored_path   TEXT NOT NULL,
              uploaded_at   TEXT NOT NULL DEFAULT (datetime('now')),
              size_bytes    INTEGER NULL,
              FOREIGN KEY(category_id) REFERENCES categories(id) ON DELETE SET NULL
            );
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS chunks (
              id           INTEGER PRIMARY KEY AUTOINCREMENT,
              document_id  INTEGER NOT NULL,
              chunk_index  INTEGER NOT NULL,
              source_label TEXT NULL,
              content      TEXT NOT NULL,
              FOREIGN KEY(document_id) REFERENCES documents(id) ON DELETE CASCADE
            );
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS chunk_embeddings (
              chunk_id       INTEGER PRIMARY KEY,
              embedding_model TEXT NOT NULL,
              embedding_dim   INTEGER NOT NULL,
              embedding_json  TEXT NOT NULL,
              FOREIGN KEY(chunk_id) REFERENCES chunks(id) ON DELETE CASCADE
            );
            """;
        cmd.ExecuteNonQuery();

        if (VecEnabled)
        {
            try
            {
                EnsureVecTableDim(Config.EmbeddingDimGuess);
            }
            catch
            {
                VecEnabled = false;
            }
        }
    }

    public void EnsureVecTableDim(int dim)
    {
        if (!VecEnabled) return;
        var target = Math.Max(1, dim);

        using var cmd = Connection.CreateCommand();

        int? current = null;
        try
        {
            cmd.CommandText = "SELECT value FROM app_kv WHERE key='vec_dim';";
            var v = cmd.ExecuteScalar() as string;
            if (int.TryParse(v, out var parsed))
                current = parsed;
        }
        catch
        {
            // ignore
        }

        bool vecTableExists;
        try
        {
            cmd.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name='chunk_vec';";
            using var reader = cmd.ExecuteReader();
            vecTableExists = reader.Read();
        }
        catch
        {
            vecTableExists = false;
        }

        if (!vecTableExists || current is null || current != target)
        {
            cmd.CommandText = "DROP TABLE IF EXISTS chunk_vec;";
            cmd.ExecuteNonQuery();

            cmd.CommandText = $"CREATE VIRTUAL TABLE chunk_vec USING vec0(embedding FLOAT[{target}]);";
            cmd.ExecuteNonQuery();

            cmd.CommandText =
                "INSERT OR REPLACE INTO app_kv(key, value) VALUES ('vec_dim', $val);";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$val", target.ToString());
            cmd.ExecuteNonQuery();
        }
    }

    public void DisableVec() => VecEnabled = false;

    public void Dispose()
    {
        try { Connection.Dispose(); }
        catch { /* ignore */ }
    }
}

