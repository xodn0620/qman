namespace QMan.Core;

/// <summary>app_kv 테이블에 저장되는 앱 설정 키.</summary>
public static class AppSettingsKeys
{
    public const string SetupComplete = "cfg.setup_complete";
    public const string LlmProviderKey = "cfg.llm.provider";
    /// <summary>구버전 단일 저장(마이그레이션·폴백용).</summary>
    public const string ChatModel = "cfg.llm.chat_model";
    public const string EmbeddingModel = "cfg.llm.embedding_model";
    public const string ApiKey = "cfg.llm.api_key";
    public const string OpenAiApiKey = "cfg.llm.openai_api_key";
    public const string EmbeddingApiKey = "cfg.llm.embedding_api_key";
    public const string Url = "cfg.llm.url";

    public const string ProfilePrefix = "cfg.llm.profile.";

    public static string ProfileChatModel(string providerTag) => $"{ProfilePrefix}{providerTag}.chat_model";
    public static string ProfileEmbeddingModel(string providerTag) => $"{ProfilePrefix}{providerTag}.embedding_model";
    public static string ProfileApiKey(string providerTag) => $"{ProfilePrefix}{providerTag}.api_key";
    public static string ProfileUrl(string providerTag) => $"{ProfilePrefix}{providerTag}.url";
    /// <summary>Claude 전용.</summary>
    public const string ProfileClaudeEmbeddingApiKey = "cfg.llm.profile.claude.embedding_api_key";

    public static readonly string[] AllProviderTags = ["openai", "ollama", "claude", "googleai", "alibabacloud"];

    public static string ProviderTag(LlmProvider p) => p switch
    {
        LlmProvider.Ollama => "ollama",
        LlmProvider.Claude => "claude",
        LlmProvider.GoogleAi => "googleai",
        LlmProvider.AlibabaCloud => "alibabacloud",
        _ => "openai"
    };
}
