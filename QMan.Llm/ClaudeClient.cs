using System.Net.Http.Json;
using System.Text.Json;
using QMan.Core;

namespace QMan.Llm;

/// <summary>Anthropic Messages API(채팅) + OpenAI Embeddings(임베딩, 별도 API 키).</summary>
public sealed class ClaudeClient : ILlmClient
{
    private readonly AppConfig _config;
    private readonly HttpClient _http;

    public ClaudeClient(AppConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        RequireEmbeddingKey();

        var body = new
        {
            model = _config.EmbeddingModel,
            input = text ?? string.Empty
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings")
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.EmbeddingApiKey);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI 임베딩 실패: HTTP {(int)resp.StatusCode} / {respBody}");

        using var doc = JsonDocument.Parse(respBody);
        var emb = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");
        var arr = new float[emb.GetArrayLength()];
        var i = 0;
        foreach (var v in emb.EnumerateArray())
            arr[i++] = (float)v.GetDouble();
        return arr;
    }

    public async Task<string> ChatAsync(string system, string user, CancellationToken ct = default)
    {
        RequireAnthropicKey();

        var messages = new[] { new { role = "user", content = user ?? string.Empty } };
        object payload = string.IsNullOrWhiteSpace(system)
            ? new { model = _config.ChatModel, max_tokens = 8192, messages }
            : new { model = _config.ChatModel, max_tokens = 8192, system, messages };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.TryAddWithoutValidation("x-api-key", _config.OpenAiApiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Claude 채팅 실패: HTTP {(int)resp.StatusCode} / {respBody}");

        using var doc = JsonDocument.Parse(respBody);
        var content = doc.RootElement.GetProperty("content");
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var t) &&
                t.GetString() == "text" &&
                block.TryGetProperty("text", out var txt))
                return txt.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private void RequireAnthropicKey()
    {
        if (string.IsNullOrWhiteSpace(_config.OpenAiApiKey))
            throw new InvalidOperationException(
                "Anthropic(Claude) API 키가 설정되지 않았습니다. 설정에서 API 키를 입력해 주세요.");
    }

    private void RequireEmbeddingKey()
    {
        if (string.IsNullOrWhiteSpace(_config.EmbeddingApiKey))
            throw new InvalidOperationException(
                "Claude 사용 시 임베딩은 OpenAI API를 사용합니다. 임베딩 API 키(OpenAI)를 설정해 주세요.");
    }
}
