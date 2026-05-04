using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using QMan.Core;

namespace QMan.Data;

public sealed class AppSettingsDao
{
    private const string ProtectedPrefix = "dpapi:v1:";
    private static readonly byte[] ProtectedEntropy = Encoding.UTF8.GetBytes("QMan.AppSettings.v1");
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
        {
            var key = rd.GetString(0);
            d[key] = DecodeStoredValue(key, rd.GetString(1));
        }
        return d;
    }

    public string? Get(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_kv WHERE key = $k LIMIT 1;";
        cmd.Parameters.AddWithValue("$k", key);
        var raw = cmd.ExecuteScalar() as string;
        return raw is null ? null : DecodeStoredValue(key, raw);
    }

    public void UpsertKey(string key, string value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO app_kv(key, value) VALUES ($k, $v);";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", EncodeStoredValue(key, value));
        cmd.ExecuteNonQuery();
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

                if (tag is "claude" or "dsplayground" && !string.IsNullOrWhiteSpace(s.ClaudeEmbeddingApiKey))
                    Upsert(tx, AppSettingsKeys.ProfileEmbeddingApiKey(tag), s.ClaudeEmbeddingApiKey.Trim());
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
                ClaudeEmbeddingApiKey = tag is "claude" or "dsplayground"
                    ? (K(kv, AppSettingsKeys.ProfileEmbeddingApiKey(tag))
                       ?? (tag == "claude" ? K(kv, AppSettingsKeys.ProfileClaudeEmbeddingApiKey) : null)
                       ?? "")
                    : ""
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

        if (string.IsNullOrWhiteSpace(r["dsplayground"].Url))
            r["dsplayground"].Url = "https://apigw-dev.aisp-shinhands.co.kr/v1";

        return r;
    }

    private static void Upsert(SqliteTransaction tx, string key, string value)
    {
        using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO app_kv(key, value) VALUES ($k, $v);";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", EncodeStoredValue(key, value));
        cmd.ExecuteNonQuery();
    }

    public static void ProtectStoredSecrets(SqliteConnection conn)
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var tx = conn.BeginTransaction();
        using var read = conn.CreateCommand();
        read.Transaction = tx;
        read.CommandText = "SELECT key, value FROM app_kv WHERE key LIKE 'cfg.%';";

        var rewrites = new List<KeyValuePair<string, string>>();
        using (var rd = read.ExecuteReader())
        {
            while (rd.Read())
            {
                var key = rd.GetString(0);
                var value = rd.GetString(1);
                if (!AppSettingsKeys.IsSensitiveKey(key))
                    continue;
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
                    continue;
                rewrites.Add(new KeyValuePair<string, string>(key, EncodeStoredValue(key, value)));
            }
        }

        foreach (var item in rewrites)
        {
            using var write = conn.CreateCommand();
            write.Transaction = tx;
            write.CommandText = "UPDATE app_kv SET value = $v WHERE key = $k;";
            write.Parameters.AddWithValue("$k", item.Key);
            write.Parameters.AddWithValue("$v", item.Value);
            write.ExecuteNonQuery();
        }

        tx.Commit();
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
                cmd.Parameters.AddWithValue("$v", EncodeStoredValue(kv.Key, kv.Value));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();

            try
            {
                File.Delete(path);
            }
            catch
            {
                // 삭제 실패 시 안전하게 파일 내용을 덮어씁니다.
                try
                {
                    File.WriteAllText(path, "{}");
                }
                catch
                {
                    // 마지막으로 아무 것도 못하면 무시
                }
            }
        }
        catch
        {
            // 손상된 JSON 등은 무시
        }
    }

    private static string EncodeStoredValue(string key, string value)
    {
        if (!AppSettingsKeys.IsSensitiveKey(key) || string.IsNullOrWhiteSpace(value))
            return value;
        if (value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            return value;
        if (!OperatingSystem.IsWindows())
            return value;

        try
        {
            return ProtectOnWindows(value);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "민감한 설정을 Windows 보호 저장소(DPAPI)로 암호화하지 못했습니다. " +
                "API 키를 평문으로 저장하지 않도록 저장을 중단합니다.", ex);
        }
    }

    private static string DecodeStoredValue(string key, string value)
    {
        if (!AppSettingsKeys.IsSensitiveKey(key) || string.IsNullOrWhiteSpace(value))
            return value;
        if (!value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            return value;
        if (!OperatingSystem.IsWindows())
            return string.Empty;

        try
        {
            return UnprotectOnWindows(value);
        }
        catch
        {
            return string.Empty;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string ProtectOnWindows(string value)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(plain, ProtectedEntropy, DataProtectionScope.CurrentUser);
        return ProtectedPrefix + Convert.ToBase64String(protectedBytes);
    }

    [SupportedOSPlatform("windows")]
    private static string UnprotectOnWindows(string value)
    {
        var payload = Convert.FromBase64String(value[ProtectedPrefix.Length..]);
        var plain = ProtectedData.Unprotect(payload, ProtectedEntropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
}
