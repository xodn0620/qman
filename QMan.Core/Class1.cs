using System.Text.Json;

namespace QMan.Core;

public enum LlmProvider
{
    OpenAi,
    Ollama
}

/// <summary>
/// 앱에서 사용하는 경로/폴더 계산 유틸리티 (Java AppPaths 대응).
/// </summary>
public static class AppPaths
{
    public static string AppHomeDir
    {
        get
        {
            if (IsPortableMode)
                return PortableRootDir;

            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userHome, "qman");
        }
    }

    public static string DataDir => Path.Combine(AppHomeDir, "data");
    public static string DocsDir => Path.Combine(AppHomeDir, "docs");
    public static string NativeDir => Path.Combine(AppHomeDir, "native");
    public static string LogsDir => Path.Combine(AppHomeDir, "logs");
    public static string DbPath => Path.Combine(DataDir, "qman.db");

    public static void EnsureDirs()
    {
        Directory.CreateDirectory(AppHomeDir);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(NativeDir);
        Directory.CreateDirectory(LogsDir);
    }

    private static bool IsPortableMode
    {
        get
        {
            try
            {
                var prop = Environment.GetEnvironmentVariable("SMQ_PORTABLE") ?? string.Empty;
                if (prop.Equals("true", StringComparison.OrdinalIgnoreCase))
                    return true;

                var baseDir = PortableRootDir;
                if (File.Exists(Path.Combine(baseDir, "portable.flag"))) return true;
                if (File.Exists(Path.Combine(baseDir, "config.json"))) return true;
            }
            catch
            {
                // ignore
            }

            return false;
        }
    }

    private static string PortableRootDir
    {
        get
        {
            // 1) 강제 지정
            var forced = Environment.GetEnvironmentVariable("SMQ_PORTABLE_ROOT");
            if (!string.IsNullOrWhiteSpace(forced))
                return forced;

            // 2) 코드 위치 기반 추정
            string guessed;
            try
            {
                guessed = AppContext.BaseDirectory;
            }
            catch
            {
                guessed = Directory.GetCurrentDirectory();
            }

            // 3) 위로 올라가며 portable.flag/config.json 찾기
            var cur = new DirectoryInfo(guessed);
            for (var i = 0; i < 6 && cur != null; i++)
            {
                try
                {
                    if (File.Exists(Path.Combine(cur.FullName, "portable.flag")) ||
                        File.Exists(Path.Combine(cur.FullName, "config.json")))
                        return cur.FullName;
                }
                catch
                {
                    // ignore
                }

                cur = cur.Parent;
            }

            return guessed;
        }
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
    public string? OpenAiApiKey { get; init; }
    public string? Url { get; init; }
    public int EmbeddingDimGuess { get; init; }
    public string? SqliteVecDllPath { get; init; }

    public static AppConfig Load()
    {
        AppPaths.EnsureDirs();

        // 1) 실행 폴더(AppHomeDir) 우선
        var cfg1 = Path.Combine(AppPaths.AppHomeDir, "config.json");
        // 2) 사용자 홈 fallback (기존 Java와 유사하게 유지하고 싶다면)
        var cfg2 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "qman",
            "config.json"
        );

        string json = "{}";
        if (File.Exists(cfg1))
        {
            json = File.ReadAllText(cfg1);
        }
        else if (File.Exists(cfg2))
        {
            json = File.ReadAllText(cfg2);
        }

        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var raw = JsonSerializer.Deserialize<RawConfig>(json, jsonOpts) ?? new RawConfig();

        // 환경변수 우선
        var providerRaw = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SMQ_LLM_PROVIDER"),
            raw.Llm?.Provider,
            "openai"
        );

        var provider = ParseProvider(providerRaw);

        var chatModel = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SMQ_LLM_CHAT_MODEL"),
            raw.Llm?.ChatModel
        );

        var embeddingModel = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SMQ_LLM_EMBEDDING_MODEL"),
            raw.Llm?.EmbeddingModel
        );

        if (string.IsNullOrWhiteSpace(chatModel))
            chatModel = provider == LlmProvider.Ollama ? "llama3.2" : "gpt-4o-mini";

        if (string.IsNullOrWhiteSpace(embeddingModel))
            embeddingModel = provider == LlmProvider.Ollama ? "nomic-embed-text" : "text-embedding-3-small";

        var apiKey = FirstNonEmpty(
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            raw.Llm?.OpenAiApiKey
        );

        var url = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SMQ_LLM_URL"),
            raw.Llm?.Url
        );

        var dimGuess = InferEmbeddingDimGuess(provider, embeddingModel);

        // sqlite-vec DLL: 환경변수 → 자동 탐색
        var vecDll = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SMQ_SQLITE_VEC_DLL"),
            raw.SqliteVec?.DllPath
        );
        if (string.IsNullOrWhiteSpace(vecDll))
            vecDll = GuessVecDllPath();

        return new AppConfig
        {
            LlmProvider = provider,
            ChatModel = chatModel,
            EmbeddingModel = embeddingModel,
            OpenAiApiKey = apiKey,
            Url = url,
            EmbeddingDimGuess = dimGuess,
            SqliteVecDllPath = vecDll
        };
    }

    private sealed class RawConfig
    {
        public LlmSection? Llm { get; set; }
        public SqliteVecSection? SqliteVec { get; set; }
    }

    private sealed class LlmSection
    {
        public string? Provider { get; set; }
        public string? ChatModel { get; set; }
        public string? EmbeddingModel { get; set; }
        public string? OpenAiApiKey { get; set; }
        public string? Url { get; set; }
    }

    private sealed class SqliteVecSection
    {
        public string? DllPath { get; set; }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static LlmProvider ParseProvider(string? raw)
    {
        var v = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return v switch
        {
            "ollama" => LlmProvider.Ollama,
            "openai" or "oa" => LlmProvider.OpenAi,
            _ => LlmProvider.OpenAi
        };
    }

    private static int InferEmbeddingDimGuess(LlmProvider provider, string embeddingModel)
    {
        if (provider == LlmProvider.OpenAi)
        {
            var m = (embeddingModel ?? string.Empty).Trim();
            if (m.Equals("text-embedding-3-large", StringComparison.OrdinalIgnoreCase)) return 3072;
            if (m.Equals("text-embedding-3-small", StringComparison.OrdinalIgnoreCase)) return 1536;
            if (m.Equals("text-embedding-ada-002", StringComparison.OrdinalIgnoreCase)) return 1536;
            return 1536;
        }

        // Ollama 대표값
        return 768;
    }

    private static string GuessVecDllPath()
    {
        var dir = AppPaths.NativeDir;
        var candidates = new[]
        {
            Path.Combine(dir, "sqlite-vec.dll"),
            Path.Combine(dir, "sqlite_vec.dll"),
            Path.Combine(dir, "vec0.dll"),
            Path.Combine(dir, "sqlitevec.dll")
        };

        foreach (var c in candidates)
        {
            try
            {
                if (File.Exists(c)) return c;
            }
            catch
            {
                // ignore
            }
        }

        return Path.Combine(dir, "sqlite-vec.dll");
    }
}

