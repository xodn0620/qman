using Microsoft.Data.Sqlite;
using QMan.Core;

namespace QMan.Data;

/// <summary>
/// SQLite 연결 및 마이그레이션/vec 확장 로딩을 담당하는 래퍼 (Java Db 대응).
/// </summary>
public sealed class SqliteDb : IDisposable
{
    public AppConfig Config { get; private set; } = null!;
    public SqliteConnection Connection { get; }
    public bool VecEnabled { get; private set; }

    public SqliteDb()
    {
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

        MigrateSchema();
        AppSettingsDao.TryImportLegacyConfigJson(Connection);
        var kv = new AppSettingsDao(Connection).LoadAll();
        Config = AppConfig.FromStoredValues(kv);
        VecDisabledReason = "";
        var vecDll = AppConfig.TryFindNativeVecDllPath();
        if (vecDll is null)
        {
            VecDisabledReason =
                $"sqlite-vec 미포함 빌드이거나 캐시에 없음 → 빌드 시 임베드 필요. 캐시: {AppConfig.SqliteVecCacheHintDir}";
        }
        else if (!TryLoadSqliteVec(Connection, vecDll, out var loadErr))
        {
            VecDisabledReason = loadErr ?? "sqlite-vec 로드 실패";
        }
        else
        {
            // chunk_vec는 실제 임베딩 차원이 확정된 뒤(EnsureVecTableDim)에만 만든다.
            // 시작 시 EmbeddingDimGuess만으로 CREATE하면 환경/추정 오류 시 전체 벡터 검색이 꺼진다(SQL logic error).
            VecEnabled = true;
        }
    }

    /// <summary>벡터 검색이 꺼진 이유(상태줄 표시용). 비어 있으면 사용 중이거나 미구성.</summary>
    public string VecDisabledReason { get; private set; } = "";

    private static bool TryLoadSqliteVec(SqliteConnection conn, string dllPath, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
        {
            error = $"DLL 경로 없음: {dllPath}";
            return false;
        }

        try
        {
            conn.EnableExtensions(true);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        // sqlite-vec 로드형 빌드는 sqlite3_extension_init 대신 sqlite3_vec_init만 노출하는 경우가 있고,
        // 파일명을 바꾸면 SQLite 기본 진입점 추론과도 어긋날 수 있어 명시 진입점을 재시도한다.
        string? firstErr = null;
        try
        {
            conn.LoadExtension(dllPath);
            return true;
        }
        catch (Exception ex)
        {
            firstErr = ex.Message;
        }

        try
        {
            conn.LoadExtension(dllPath, "sqlite3_vec_init");
            return true;
        }
        catch (Exception ex2)
        {
            error = firstErr is null
                ? ex2.Message
                : firstErr + " → sqlite3_vec_init: " + ex2.Message;
            return false;
        }
    }

    private void MigrateSchema()
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

        MigrateCategorySortOrder(cmd);

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

        MigrateDropUnusedSchemaObjects();
    }

    /// <summary>
    /// 앱이 사용하지 않는 테이블·뷰를 제거합니다(이전 버전/다른 도구로 생긴 고아 객체 정리).
    /// </summary>
    private void MigrateDropUnusedSchemaObjects()
    {
        var keepTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "app_kv",
            "categories",
            "documents",
            "chunks",
            "chunk_embeddings",
            "chunk_vec"
        };

        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = OFF;";
        cmd.ExecuteNonQuery();

        var views = new List<string>();
        cmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type='view' AND name NOT LIKE 'sqlite_%';";
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
                views.Add(rd.GetString(0));
        }

        foreach (var v in views)
        {
            cmd.CommandText = $"DROP VIEW IF EXISTS \"{EscapeSqlIdent(v)}\";";
            cmd.ExecuteNonQuery();
        }

        var tables = new List<string>();
        cmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
                tables.Add(rd.GetString(0));
        }

        foreach (var t in tables)
        {
            if (keepTables.Contains(t))
                continue;
            cmd.CommandText = $"DROP TABLE IF EXISTS \"{EscapeSqlIdent(t)}\";";
            cmd.ExecuteNonQuery();
        }

        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();

        using (var norm = Connection.CreateCommand())
        {
            norm.CommandText =
                """
                UPDATE app_kv SET value = 'alibabacloud'
                WHERE key = $k AND lower(trim(value)) IN ('qwen3', 'qwen', 'dashscope');
                """;
            norm.Parameters.AddWithValue("$k", AppSettingsKeys.LlmProviderKey);
            norm.ExecuteNonQuery();
        }
    }

    private static string EscapeSqlIdent(string name)
        => name.Replace("\"", "\"\"", StringComparison.Ordinal);

    private static void MigrateCategorySortOrder(SqliteCommand cmd)
    {
        var hasSort = false;
        cmd.CommandText = "PRAGMA table_info(categories);";
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
            {
                if (string.Equals(rd.GetString(1), "sort_order", StringComparison.Ordinal))
                    hasSort = true;
            }
        }

        if (hasSort)
            return;

        cmd.CommandText = "ALTER TABLE categories ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0;";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT id FROM categories ORDER BY created_at ASC, id ASC;";
        var ids = new List<long>();
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
                ids.Add(rd.GetInt64(0));
        }

        for (var i = 0; i < ids.Count; i++)
        {
            cmd.CommandText = "UPDATE categories SET sort_order = $s WHERE id = $id;";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$s", i);
            cmd.Parameters.AddWithValue("$id", ids[i]);
            cmd.ExecuteNonQuery();
        }
    }

    public void EnsureVecTableDim(int dim)
    {
        if (!VecEnabled || dim <= 0) return;
        var target = dim;

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

        // 가상 테이블만 남고 sqlite-vec 그림자 테이블이 없으면 INSERT 시
        // "could not initialize 'insert rowids' statement" 등으로 실패한다.
        var rowidsShadowOk = false;
        if (vecTableExists)
        {
            try
            {
                cmd.CommandText =
                    "SELECT 1 FROM sqlite_master WHERE type='table' AND name='chunk_vec_rowids' LIMIT 1;";
                rowidsShadowOk = cmd.ExecuteScalar() is not null;
            }
            catch
            {
                rowidsShadowOk = false;
            }
        }

        var needsRebuild = !vecTableExists || !rowidsShadowOk || current is null || current != target;
        if (needsRebuild)
        {
            cmd.CommandText = "DROP TABLE IF EXISTS chunk_vec;";
            cmd.ExecuteNonQuery();

            // sqlite-vec 문서는 float[N] 소문자 권장. 일부 빌드에서 FLOAT 대문자 조합이 SQL logic error를 낼 수 있음.
            cmd.CommandText = $"CREATE VIRTUAL TABLE chunk_vec USING vec0(embedding float[{target}]);";
            cmd.ExecuteNonQuery();

            cmd.CommandText =
                "INSERT OR REPLACE INTO app_kv(key, value) VALUES ('vec_dim', $val);";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$val", target.ToString());
            cmd.ExecuteNonQuery();
        }
    }

    public void DisableVec(string? reason = null)
    {
        VecEnabled = false;
        if (!string.IsNullOrWhiteSpace(reason))
            VecDisabledReason = reason;
    }

    public void Dispose()
    {
        try { Connection.Dispose(); }
        catch { /* ignore */ }
    }
}

