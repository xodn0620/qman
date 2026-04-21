namespace QMan.Core;

/// <summary>
/// 설정 UI에서 "제공자"를 고르지 않고, API URL·키로 LLM 백엔드를 추론합니다.
/// </summary>
public static class LlmEndpointInference
{
    /// <param name="url">비우면 각 클라이언트가 기본 URL 사용.</param>
    /// <param name="apiKey">메인 API 키(Anthropic/OpenAI/Gemini/DashScope 등).</param>
    /// <param name="claudeEmbeddingApiKey">Claude 사용 시 OpenAI 임베딩용(선택, 추론 보조).</param>
    public static LlmProvider Infer(string? url, string? apiKey, string? claudeEmbeddingApiKey = null)
    {
        var u = (url ?? "").Trim();
        var k = (apiKey ?? "").Trim();

        if (Uri.TryCreate(u, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.ToLowerInvariant();
            var port = uri.Port;

            if (port == 11434 || host.Contains("ollama", StringComparison.Ordinal))
                return LlmProvider.Ollama;

            if (host.Contains("anthropic.com", StringComparison.Ordinal))
                return LlmProvider.Claude;

            if (host.Contains("googleapis.com", StringComparison.Ordinal) ||
                host.Contains("generativelanguage.googleapis.com", StringComparison.Ordinal))
                return LlmProvider.GoogleAi;

            if (host.Contains("dashscope.aliyuncs.com", StringComparison.Ordinal) ||
                host.Contains("dashscope", StringComparison.OrdinalIgnoreCase) ||
                host.Contains("aliyuncs.com", StringComparison.Ordinal))
                return LlmProvider.AlibabaCloud;

            if (host.Contains("openai.com", StringComparison.Ordinal) ||
                host.Contains("azure.com", StringComparison.Ordinal))
                return LlmProvider.OpenAi;

            // OpenAI 호환 프록시(LiteLLM 등)는 흔히 localhost 비-11434
            if (host is "localhost" or "127.0.0.1")
                return port == 11434 ? LlmProvider.Ollama : LlmProvider.OpenAi;

            return LlmProvider.OpenAi;
        }

        if (k.StartsWith("sk-ant", StringComparison.Ordinal))
            return LlmProvider.Claude;

        if (k.StartsWith("AIza", StringComparison.Ordinal) && k.Length >= 20)
            return LlmProvider.GoogleAi;

        // URL 없음: 키 없으면 로컬 Ollama(기본 localhost), 키 있으면 OpenAI 기본 엔드포인트 전제
        if (string.IsNullOrEmpty(u))
        {
            if (string.IsNullOrEmpty(k))
                return LlmProvider.Ollama;
            return LlmProvider.OpenAi;
        }

        return LlmProvider.OpenAi;
    }
}
