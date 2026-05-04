using System.Net.Http.Json;
using System.Text.Json;
using QMan.Core;

namespace QMan.Llm;

/// <summary>Alibaba Cloud DashScope: OpenAI 호환 채팅 + 텍스트 임베딩 API.</summary>
public sealed class AlibabaCloudClient : ILlmClient
{
    private readonly AppConfig _config;
    private readonly HttpClient _http;

    public AlibabaCloudClient(AppConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        RequireKey();

        var body = new
        {
            model = _config.EmbeddingModel,
            input = new { texts = new[] { text ?? string.Empty } }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://dashscope.aliyuncs.com/api/v1/services/embeddings/text-embedding/text-embedding")
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.OpenAiApiKey);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw LlmHttpErrors.HttpFailure("Alibaba Cloud", "임베딩", resp.StatusCode, respBody);

        using var doc = JsonDocument.Parse(respBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("output", out var output))
        {
            if (output.TryGetProperty("embeddings", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var first = arr[0];
                if (first.ValueKind == JsonValueKind.Array)
                    return ParseFloatArray(first);
                if (first.TryGetProperty("embedding", out var embObj))
                    return ParseFloatArray(embObj);
            }

            if (output.TryGetProperty("embedding", out var single) && single.ValueKind == JsonValueKind.Array)
                return ParseFloatArray(single);
        }

        throw LlmHttpErrors.ParseFailure("Alibaba Cloud", "임베딩");
    }

    public async Task<string> ChatAsync(string system, string user, CancellationToken ct = default)
    {
        RequireKey();

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(system))
            messages.Add(new { role = "system", content = system });
        messages.Add(new { role = "user", content = user ?? string.Empty });

        var body = new { model = _config.ChatModel, messages };

        using var req = new HttpRequestMessage(HttpMethod.Post, ResolveChatUrl())
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.OpenAiApiKey);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw LlmHttpErrors.HttpFailure("Alibaba Cloud", "채팅", resp.StatusCode, respBody);

        using var doc = JsonDocument.Parse(respBody);
        var root = doc.RootElement;
        var content = root.GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
        return content;
    }

    private string ResolveChatUrl()
    {
        var u = AppConfig.ResolveUserSuppliedBaseUrl(_config.Url, "https://dashscope.aliyuncs.com/compatible-mode/v1");
        if (u.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) return u + "/chat/completions";
        if (u.EndsWith("/")) return u + "chat/completions";
        if (u.Contains("/chat/", StringComparison.OrdinalIgnoreCase)) return u;
        return u + "/chat/completions";
    }

    private void RequireKey()
    {
        if (string.IsNullOrWhiteSpace(_config.OpenAiApiKey))
            throw new InvalidOperationException(
                "Alibaba Cloud API 키가 설정되지 않았습니다. 설정에서 API 키를 입력해 주세요.");
    }

    private static float[] ParseFloatArray(JsonElement arr)
    {
        var v = new float[arr.GetArrayLength()];
        var i = 0;
        foreach (var e in arr.EnumerateArray())
            v[i++] = (float)e.GetDouble();
        return v;
    }
}
