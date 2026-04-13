namespace QMan.Core;

public enum LlmProvider
{
    OpenAi,
    Ollama,
    Claude,
    GoogleAi,
    AlibabaCloud
}

/// <summary>
/// 앱에서 사용하는 경로/폴더 계산 유틸리티 (Java AppPaths 대응).
/// 항상 실행 파일이 있는 폴더가 앱 홈 — qman.db 는 그 아래 data\qman.db 만 사용합니다(다른 경로 조회·복사 없음).
/// </summary>
public static class AppPaths
{
    /// <summary>실행 어셈블리 기준 디렉터리(배포 시 QMan.exe와 같은 폴더).</summary>
    public static string AppHomeDir
    {
        get
        {
            try
            {
                return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return Directory.GetCurrentDirectory();
            }
        }
    }

    public static string DataDir => Path.Combine(AppHomeDir, "data");
    public static string DbPath => Path.Combine(DataDir, "qman.db");

    /// <summary>
    /// 임베드된 sqlite-vec 네이티브 DLL을 풀어두는 경로(QMan.exe 옆이 아님).
    /// SQLite LoadExtension은 Windows에서 파일 경로가 필요해 메모리만으로는 로드할 수 없습니다.
    /// </summary>
    public static string SqliteVecCacheDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QMan",
            "vec");

    public static void EnsureDirs()
    {
        Directory.CreateDirectory(AppHomeDir);
        Directory.CreateDirectory(DataDir);
    }
}

/// <summary>
/// LLM/벡터 검색 등 앱 설정 로딩 (Java Config 대응).
/// </summary>
public sealed class AppConfig
{
    public LlmProvider LlmProvider { get; init; }
    public string ChatModel { get; init; } = string.Empty;
    public string EmbeddingModel { get; init; } = string.Empty;
    /// <summary>제공자별 메인 API 키(OpenAI / Anthropic / DashScope).</summary>
    public string? OpenAiApiKey { get; init; }
    /// <summary>Claude 전용: OpenAI 임베딩 API 키.</summary>
    public string? EmbeddingApiKey { get; init; }
    public string? Url { get; init; }
    public int EmbeddingDimGuess { get; init; }

    /// <summary>app_kv(cfg.*)과 환경 변수를 합쳐 런타임 설정을 만듭니다.</summary>
    public static AppConfig FromStoredValues(IReadOnlyDictionary<string, string> kv)
    {
        AppPaths.EnsureDirs();

        static string? FromKv(IReadOnlyDictionary<string, string> d, string key) =>
            d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        var providerRaw = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SMQ_LLM_PROVIDER"),
            FromKv(kv, AppSettingsKeys.LlmProviderKey),
            "openai");

        var provider = ParseLlmProvider(providerRaw);

        var ptag = AppSettingsKeys.ProviderTag(provider);

        var chatModel = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SMQ_LLM_CHAT_MODEL"),
            FromKv(kv, AppSettingsKeys.ProfileChatModel(ptag)),
            FromKv(kv, AppSettingsKeys.ChatModel));

        var embeddingModel = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SMQ_LLM_EMBEDDING_MODEL"),
            FromKv(kv, AppSettingsKeys.ProfileEmbeddingModel(ptag)),
            FromKv(kv, AppSettingsKeys.EmbeddingModel));

        if (string.IsNullOrWhiteSpace(chatModel))
            chatModel = DefaultChatModel(provider);

        if (string.IsNullOrWhiteSpace(embeddingModel))
            embeddingModel = DefaultEmbeddingModel(provider);

        var apiKey = ResolveMainApiKey(provider, kv);
        var embedOnlyKey = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SMQ_EMBEDDING_API_KEY"),
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            FromKv(kv, AppSettingsKeys.ProfileClaudeEmbeddingApiKey),
            FromKv(kv, AppSettingsKeys.EmbeddingApiKey));

        var url = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SMQ_LLM_URL"),
            FromKv(kv, AppSettingsKeys.ProfileUrl(ptag)),
            FromKv(kv, AppSettingsKeys.Url));

        var dimGuess = InferEmbeddingDimGuess(provider, embeddingModel);

        return new AppConfig
        {
            LlmProvider = provider,
            ChatModel = chatModel,
            EmbeddingModel = embeddingModel,
            OpenAiApiKey = apiKey,
            EmbeddingApiKey = provider == LlmProvider.Claude ? embedOnlyKey : null,
            Url = url,
            EmbeddingDimGuess = dimGuess
        };
    }

    public static string DefaultChatModel(LlmProvider provider) =>
        provider switch
        {
            LlmProvider.Ollama => "llama3.2",
            LlmProvider.Claude => "claude-sonnet-4-20250514",
            LlmProvider.GoogleAi => "gemini-2.5-flash-lite",
            LlmProvider.AlibabaCloud => "qwen3-max",
            _ => "gpt-4o-mini"
        };

    public static string DefaultEmbeddingModel(LlmProvider provider) =>
        provider switch
        {
            LlmProvider.Ollama => "nomic-embed-text",
            LlmProvider.Claude => "text-embedding-3-small",
            LlmProvider.GoogleAi => "gemini-embedding-001",
            LlmProvider.AlibabaCloud => "text-embedding-v3",
            _ => "text-embedding-3-small"
        };

    private static string? ResolveMainApiKey(LlmProvider provider, IReadOnlyDictionary<string, string> kv)
    {
        static string? K(IReadOnlyDictionary<string, string> d, string key) =>
            d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        var ptag = AppSettingsKeys.ProviderTag(provider);
        var fromFile = FirstNonEmpty(
            K(kv, AppSettingsKeys.ProfileApiKey(ptag)),
            K(kv, AppSettingsKeys.ApiKey),
            K(kv, AppSettingsKeys.OpenAiApiKey));

        return provider switch
        {
            LlmProvider.OpenAi => FirstNonEmpty(
                Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
                Environment.GetEnvironmentVariable("SMQ_LLM_API_KEY"),
                fromFile),
            LlmProvider.Claude => FirstNonEmpty(
                Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
                Environment.GetEnvironmentVariable("SMQ_LLM_API_KEY"),
                fromFile),
            LlmProvider.GoogleAi => FirstNonEmpty(
                Environment.GetEnvironmentVariable("GEMINI_API_KEY"),
                Environment.GetEnvironmentVariable("GOOGLE_API_KEY"),
                Environment.GetEnvironmentVariable("SMQ_LLM_API_KEY"),
                fromFile),
            LlmProvider.AlibabaCloud => FirstNonEmpty(
                Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY"),
                Environment.GetEnvironmentVariable("SMQ_LLM_API_KEY"),
                fromFile),
            _ => fromFile
        };
    }

    /// <summary>
    /// sqlite-vec DLL 경로를 찾습니다. 임베드 풀림 캐시(%LocalAppData%\QMan\vec\)만 봅니다.
    /// </summary>
    public static string? TryFindNativeVecDllPath()
    {
        var fileNames = new[]
        {
            "sqlite-vec.dll",
            "sqlite_vec.dll",
            "vec0.dll",
            "vel0.dll",
            "sqlitevec.dll"
        };

        var root = AppPaths.SqliteVecCacheDir;
        foreach (var name in fileNames)
        {
            try
            {
                var full = Path.Combine(root, name);
                if (File.Exists(full))
                    return full;
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    /// <summary>sqlite-vec DLL 캐시 경로(안내용).</summary>
    public static string SqliteVecCacheHintDir => AppPaths.SqliteVecCacheDir;

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    public static LlmProvider ParseLlmProvider(string? raw)
    {
        var v = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return v switch
        {
            "ollama" => LlmProvider.Ollama,
            "claude" or "anthropic" => LlmProvider.Claude,
            "googleai" or "google" or "google_ai" or "gemini" => LlmProvider.GoogleAi,
            "alibabacloud" or "alibaba" or "alibaba_cloud" or "qwen3" or "qwen" or "dashscope" or "dsplayground" =>
                LlmProvider.AlibabaCloud,
            "openai" or "oa" => LlmProvider.OpenAi,
            _ => LlmProvider.OpenAi
        };
    }

    private static int InferEmbeddingDimGuess(LlmProvider provider, string embeddingModel)
    {
        var m = (embeddingModel ?? string.Empty).Trim();

        if (provider is LlmProvider.OpenAi or LlmProvider.Claude)
        {
            if (m.Equals("text-embedding-3-large", StringComparison.OrdinalIgnoreCase)) return 3072;
            if (m.Equals("text-embedding-3-small", StringComparison.OrdinalIgnoreCase)) return 1536;
            if (m.Equals("text-embedding-ada-002", StringComparison.OrdinalIgnoreCase)) return 1536;
            return 1536;
        }

        if (provider == LlmProvider.AlibabaCloud)
        {
            if (m.Contains("v4", StringComparison.OrdinalIgnoreCase)) return 1024;
            if (m.Contains("v3", StringComparison.OrdinalIgnoreCase)) return 1024;
            if (m.Contains("v2", StringComparison.OrdinalIgnoreCase)) return 1536;
            return 1024;
        }

        // GoogleAiClient가 embedContent에 output_dimensionality=768을 보냄.
        if (provider == LlmProvider.GoogleAi)
            return 768;

        // Ollama: 모델별 차원(추정). 불일치 시 검색 단계에서 EnsureVecTableDim(실제 길이)로 보정됨.
        if (m.Contains("minilm", StringComparison.OrdinalIgnoreCase)) return 384;
        if (m.Contains("mxbai", StringComparison.OrdinalIgnoreCase)) return 1024;
        if (m.Contains("bge-m3", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("bge-large", StringComparison.OrdinalIgnoreCase)) return 1024;
        if (m.Contains("nomic", StringComparison.OrdinalIgnoreCase)) return 768;
        if (m.Contains("snowflake", StringComparison.OrdinalIgnoreCase) &&
            m.Contains("xs", StringComparison.OrdinalIgnoreCase)) return 384;
        if (m.Contains("snowflake", StringComparison.OrdinalIgnoreCase)) return 1024;
        return 768;
    }
}

