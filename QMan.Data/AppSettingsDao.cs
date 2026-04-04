using System.Text.Json;
using Microsoft.Data.Sqlite;
using QMan.Core;

namespace QMan.Data;

public sealed class AppSettingsDao
{
    private readonly SqliteConnection _conn;

    public AppSettingsDao(SqliteConnection conn) => _conn = conn;

    public static bool IsSetupComplete(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_kv WHERE key = $k LIMIT 1;";
        cmd.Parameters.AddWithValue("$k", AppSettingsKeys.SetupComplete);
        var v = cmd.ExecuteScalar() as string;
        return v == "1";
    }

    public Dictionary<string, string> LoadAll()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM app_kv WHERE key LIKE 'cfg.%';";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            d[rd.GetString(0)] = rd.GetString(1);
        return d;
    }

    public string? Get(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_kv WHERE key = $k LIMIT 1;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>제공자별 프로필을 저장하고, 구버전 단일 키(cfg.llm.chat_model 등)는 제거합니다.</summary>
    public void SaveAllProviderProfiles(
        IReadOnlyDictionary<string, LlmProviderFormState> states,
        string activeProviderTag,
        bool markSetupComplete)
    {
        using var tx = _conn.BeginTransaction();
        try
        {
            foreach (var tag in AppSettingsKeys.AllProviderTags)
            {
                if (!states.TryGetValue(tag, out var s))
                    continue;

                Upsert(tx, AppSettingsKeys.ProfileChatModel(tag), s.ChatModel.Trim());
                Upsert(tx, AppSettingsKeys.ProfileEmbeddingModel(tag), s.EmbeddingModel.Trim());
                Upsert(tx, AppSettingsKeys.ProfileUrl(tag), s.Url.Trim());

                if (!string.IsNullOrWhiteSpace(s.MainApiKey))
                    Upsert(tx, AppSettingsKeys.ProfileApiKey(tag), s.MainApiKey.Trim());

                if (tag == "claude" && !string.IsNullOrWhiteSpace(s.ClaudeEmbeddingApiKey))
                    Upsert(tx, AppSettingsKeys.ProfileClaudeEmbeddingApiKey, s.ClaudeEmbeddingApiKey.Trim());
            }

            Upsert(tx, AppSettingsKeys.LlmProviderKey, activeProviderTag);

            DeleteKey(tx, AppSettingsKeys.ChatModel);
            DeleteKey(tx, AppSettingsKeys.EmbeddingModel);
            DeleteKey(tx, AppSettingsKeys.ApiKey);
            DeleteKey(tx, AppSettingsKeys.OpenAiApiKey);
            DeleteKey(tx, AppSettingsKeys.EmbeddingApiKey);
            DeleteKey(tx, AppSettingsKeys.Url);

            if (markSetupComplete)
                Upsert(tx, AppSettingsKeys.SetupComplete, "1");
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void DeleteKey(SqliteTransaction tx, string key)
    {
        using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM app_kv WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.ExecuteNonQuery();
    }

    /// <summary>DB(cfg.*)에서 제공자별 폼 상태를 읽고, 비어 있으면 구버전 단일 키·기본 모델로 채웁니다.</summary>
    public static Dictionary<string, LlmProviderFormState> LoadProviderFormStates(IReadOnlyDictionary<string, string> kv)
    {
        static string? K(IReadOnlyDictionary<string, string> d, string key) =>
            d.TryGetValue(key, out var v) ? v : null;

        static string? FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        var r = new Dictionary<string, LlmProviderFormState>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in AppSettingsKeys.AllProviderTags)
        {
            r[tag] = new LlmProviderFormState
            {
                ChatModel = K(kv, AppSettingsKeys.ProfileChatModel(tag)) ?? "",
                EmbeddingModel = K(kv, AppSettingsKeys.ProfileEmbeddingModel(tag)) ?? "",
                Url = K(kv, AppSettingsKeys.ProfileUrl(tag)) ?? "",
                MainApiKey = K(kv, AppSettingsKeys.ProfileApiKey(tag)) ?? "",
                ClaudeEmbeddingApiKey =
                    tag == "claude" ? (K(kv, AppSettingsKeys.ProfileClaudeEmbeddingApiKey) ?? "") : ""
            };
        }

        var anyProfileChat = r.Values.Any(v => !string.IsNullOrWhiteSpace(v.ChatModel));
        if (!anyProfileChat)
        {
            var active = AppConfig.ParseLlmProvider(K(kv, AppSettingsKeys.LlmProviderKey));
            var atag = AppSettingsKeys.ProviderTag(active);
            var seed = r[atag];
            seed.ChatModel = K(kv, AppSettingsKeys.ChatModel) ?? seed.ChatModel;
            seed.EmbeddingModel = K(kv, AppSettingsKeys.EmbeddingModel) ?? seed.EmbeddingModel;
            seed.Url = K(kv, AppSettingsKeys.Url) ?? seed.Url;
            var legacyMain = FirstNonEmpty(K(kv, AppSettingsKeys.ApiKey), K(kv, AppSettingsKeys.OpenAiApiKey));
            if (!string.IsNullOrWhiteSpace(legacyMain))
                seed.MainApiKey = legacyMain!;
            var emb = K(kv, AppSettingsKeys.EmbeddingApiKey);
            if (!string.IsNullOrWhiteSpace(emb))
                r["claude"].ClaudeEmbeddingApiKey = emb!;
        }

        foreach (var tag in AppSettingsKeys.AllProviderTags)
        {
            var p = AppConfig.ParseLlmProvider(tag);
            var st = r[tag];
            if (string.IsNullOrWhiteSpace(st.ChatModel))
                st.ChatModel = AppConfig.DefaultChatModel(p);
            if (string.IsNullOrWhiteSpace(st.EmbeddingModel))
                st.EmbeddingModel = AppConfig.DefaultEmbeddingModel(p);
        }

        return r;
    }

    private static void Upsert(SqliteTransaction tx, string key, string value)
    {
        using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO app_kv(key, value) VALUES ($k, $v);";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>기존 config.json이 있으면 DB로 한 번 옮기고 파일을 삭제합니다.</summary>
    public static void TryImportLegacyConfigJson(SqliteConnection conn)
    {
        if (IsSetupComplete(conn))
            return;

        var path = Path.Combine(AppPaths.AppHomeDir, "config.json");
        if (!File.Exists(path))
            return;

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var toSave = new Dictionary<string, string>(StringComparer.Ordinal);

            if (root.TryGetProperty("llm", out var llm))
            {
                if (llm.TryGetProperty("provider", out var p) && p.ValueKind == JsonValueKind.String)
                    toSave[AppSettingsKeys.LlmProviderKey] = p.GetString() ?? "";
                if (llm.TryGetProperty("chatModel", out var cm) && cm.ValueKind == JsonValueKind.String)
                    toSave[AppSettingsKeys.ChatModel] = cm.GetString() ?? "";
                if (llm.TryGetProperty("embeddingModel", out var em) && em.ValueKind == JsonValueKind.String)
                    toSave[AppSettingsKeys.EmbeddingModel] = em.GetString() ?? "";
                if (llm.TryGetProperty("openAiApiKey", out var k) && k.ValueKind == JsonValueKind.String)
                    toSave[AppSettingsKeys.OpenAiApiKey] = k.GetString() ?? "";
                if (llm.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                    toSave[AppSettingsKeys.Url] = u.GetString() ?? "";
            }

            if (toSave.Count == 0)
                return;

            toSave[AppSettingsKeys.SetupComplete] = "1";

            using var tx = conn.BeginTransaction();
            foreach (var kv in toSave)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR REPLACE INTO app_kv(key, value) VALUES ($k, $v);";
                cmd.Parameters.AddWithValue("$k", kv.Key);
                cmd.Parameters.AddWithValue("$v", kv.Value);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();

            try
            {
                File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }
        catch
        {
            // 손상된 JSON 등은 무시
        }
    }
}
