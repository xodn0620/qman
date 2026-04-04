namespace QMan.Core;

/// <summary>설정 UI·DB에 저장되는 제공자별 LLM 폼 상태.</summary>
public sealed class LlmProviderFormState
{
    public string ChatModel { get; set; } = "";
    public string EmbeddingModel { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>메인 API 키(빈 문자열이면 저장 시 기존 DB 값 유지).</summary>
    public string MainApiKey { get; set; } = "";
    /// <summary>Claude 전용 OpenAI 임베딩 키(다른 제공자는 무시).</summary>
    public string ClaudeEmbeddingApiKey { get; set; } = "";
}
