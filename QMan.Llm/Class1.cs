using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using QMan.Core;

namespace QMan.Llm;

public interface ILlmClient
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    Task<string> ChatAsync(string system, string user, CancellationToken ct = default);
}

/// <summary>
/// OpenAI API 클라이언트 (chat / embeddings).
/// </summary>
public sealed class OpenAiClient : ILlmClient
{
    private readonly AppConfig _config;
    private readonly HttpClient _http;

    public OpenAiClient(AppConfig config)
    {
        _config = config;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        RequireEmbeddingKey();

        var body = new
        {
            model = _config.EmbeddingModel,
            input = text ?? string.Empty
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, ResolveEmbeddingUrl())
        {
            Content = JsonContent.Create(body)
        };
        var embKey = ResolveEmbeddingAuthKey()!;
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", embKey);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI Embeddings 실패: HTTP {(int)resp.StatusCode} / {respBody}");

        using var doc = JsonDocument.Parse(respBody);
        var root = doc.RootElement;
        var emb = root.GetProperty("data")[0].GetProperty("embedding");

        var arr = new float[emb.GetArrayLength()];
        var i = 0;
        foreach (var v in emb.EnumerateArray())
            arr[i++] = (float)v.GetDouble();
        return arr;
    }

    public async Task<string> ChatAsync(string system, string user, CancellationToken ct = default)
    {
        RequireKey();

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(system))
        {
            messages.Add(new { role = "system", content = system });
        }

        messages.Add(new { role = "user", content = user ?? string.Empty });

        var body = new
        {
            model = _config.ChatModel,
            messages
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, ResolveChatUrl())
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.OpenAiApiKey);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI Chat 실패: HTTP {(int)resp.StatusCode} / {respBody}");

        using var doc = JsonDocument.Parse(respBody);
        var root = doc.RootElement;
        var content = root.GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
        return content;
    }

    private void RequireKey()
    {
        if (string.IsNullOrWhiteSpace(_config.OpenAiApiKey))
            throw new InvalidOperationException(
                "LLM API 키가 설정되지 않았습니다. 설정에서 LLM API 키를 입력하거나 환경변수 OPENAI_API_KEY/ SMQ_LLM_API_KEY를 설정해 주세요.");
    }

    private void RequireEmbeddingKey()
    {
        if (string.IsNullOrWhiteSpace(ResolveEmbeddingAuthKey()))
            throw new InvalidOperationException(
                _config.LlmProvider == LlmProvider.DsPlayground
                    ? "임베딩 API 키가 설정되지 않았습니다. 설정에 임베딩 API 키를 넣거나 SMQ_EMBEDDING_API_KEY 를 설정해 주세요."
                    : "API 키가 설정되지 않았습니다. 설정에서 API 키를 입력하거나 환경변수 OPENAI_API_KEY를 설정해 주세요.");
    }

    /// <summary>임베딩 엔드포인트용 Bearer( DS Playground 는 임베딩 전용 키, 그 외 OpenAI는 메인 키).</summary>
    private string? ResolveEmbeddingAuthKey()
    {
        if (!string.IsNullOrWhiteSpace(_config.EmbeddingApiKey))
            return _config.EmbeddingApiKey;
        if (_config.LlmProvider == LlmProvider.DsPlayground)
            return null;
        return _config.OpenAiApiKey;
    }

    private string ResolveChatUrl()
    {
        var u = _config.Url;
        if (string.IsNullOrWhiteSpace(u))
            return "https://api.openai.com/v1/chat/completions";
        u = u.Trim();
        if (u.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) return u + "/chat/completions";
        if (u.EndsWith("/")) return u + "chat/completions";
        if (u.Contains("/chat/", StringComparison.OrdinalIgnoreCase)) return u;
        return u + "/chat/completions";
    }

    private string ResolveEmbeddingUrl()
    {
        var u = _config.Url;
        if (string.IsNullOrWhiteSpace(u))
            return "https://api.openai.com/v1/embeddings";
        u = u.Trim();
        if (u.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) return u + "/embeddings";
        if (u.EndsWith("/")) return u + "embeddings";
        if (u.Contains("/embeddings", StringComparison.OrdinalIgnoreCase)) return u;
        return u + "/embeddings";
    }
}

/// <summary>
/// Ollama HTTP API 클라이언트 (로컬/offline).
/// </summary>
public sealed class OllamaClient : ILlmClient
{
    private readonly AppConfig _config;
    private readonly HttpClient _http;

    public OllamaClient(AppConfig config)
    {
        _config = config;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        // /api/embed 우선, 실패 시 legacy /api/embeddings로 fallback
        var body = new
        {
            model = _config.EmbeddingModel,
            input = text ?? string.Empty
        };

        var url = ResolveEmbeddingUrl();

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            // legacy 엔드포인트로 재시도
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound &&
                IsLikelyEmbedEndpoint(url))
            {
                return await EmbedLegacyAsync(text ?? string.Empty, ct).ConfigureAwait(false);
            }

            throw new InvalidOperationException($"Ollama Embedding 실패: HTTP {(int)resp.StatusCode} / {respBody}");
        }

        using var doc = JsonDocument.Parse(respBody);
        var emb = ResolveOllamaEmbeddingArray(doc.RootElement, respBody);

        var v = new float[emb.GetArrayLength()];
        var i = 0;
        foreach (var e in emb.EnumerateArray())
            v[i++] = (float)e.GetDouble();
        return v;
    }

    /// <summary>
    /// /api/embed: embeddings([[...]]) 또는 embedding([...]), 빈 배열·data[].embedding 등 처리.
    /// </summary>
    private static JsonElement ResolveOllamaEmbeddingArray(JsonElement root, string rawForError)
    {
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array &&
            data.GetArrayLength() > 0)
        {
            var row = data[0];
            if (row.ValueKind == JsonValueKind.Object &&
                row.TryGetProperty("embedding", out var embIn) && embIn.ValueKind == JsonValueKind.Array)
                return embIn;
        }

        if (root.TryGetProperty("embeddings", out var embeddings) && embeddings.ValueKind == JsonValueKind.Array)
        {
            var len = embeddings.GetArrayLength();
            if (len == 0)
                throw new InvalidOperationException("Ollama: embeddings 배열이 비었습니다. " + rawForError);
            var first = embeddings[0];
            if (first.ValueKind == JsonValueKind.Array)
                return first;
            if (first.ValueKind == JsonValueKind.Number)
                return embeddings;
            throw new InvalidOperationException("Ollama: embeddings[0] 형식을 지원하지 않습니다. " + rawForError);
        }

        if (root.TryGetProperty("embedding", out var single) && single.ValueKind == JsonValueKind.Array)
            return single;

        throw new InvalidOperationException("Ollama embedding 응답 파싱 실패: " + rawForError);
    }

    private async Task<float[]> EmbedLegacyAsync(string text, CancellationToken ct)
    {
        var body = new
        {
            model = _config.EmbeddingModel,
            prompt = text ?? string.Empty
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, ResolveEmbeddingUrlLegacy())
        {
            Content = JsonContent.Create(body)
        };

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama Embeddings(legacy) 실패: HTTP {(int)resp.StatusCode} / {respBody}");

        using var doc = JsonDocument.Parse(respBody);
        var root = doc.RootElement;
        var emb = root.GetProperty("embedding");

        var v = new float[emb.GetArrayLength()];
        var i = 0;
        foreach (var e in emb.EnumerateArray())
            v[i++] = (float)e.GetDouble();
        return v;
    }

    public async Task<string> ChatAsync(string system, string user, CancellationToken ct = default)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(system))
        {
            messages.Add(new { role = "system", content = system });
        }

        messages.Add(new { role = "user", content = user ?? string.Empty });

        var body = new
        {
            model = _config.ChatModel,
            stream = false,
            messages
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, ResolveChatUrl())
        {
            Content = JsonContent.Create(body)
        };

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama Chat 실패: HTTP {(int)resp.StatusCode} / {respBody}");

        using var doc = JsonDocument.Parse(respBody);
        var root = doc.RootElement;

        // /api/chat: { message: { content } } or { response: "..." }
        var content = root.TryGetProperty("message", out var msg)
            ? msg.GetProperty("content").GetString()
            : root.GetProperty("response").GetString();

        return content ?? string.Empty;
    }

    private string ResolveChatUrl()
    {
        var u = _config.Url;
        if (string.IsNullOrWhiteSpace(u)) return "http://localhost:11434/api/chat";
        u = u.Trim();
        if (u.EndsWith("/")) return u + "api/chat";
        if (u.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) return u + "/chat";
        if (u.Contains("/api/chat", StringComparison.OrdinalIgnoreCase)) return u;
        return u + "/api/chat";
    }

    private string ResolveEmbeddingUrl()
    {
        var u = _config.Url;
        if (string.IsNullOrWhiteSpace(u)) return "http://localhost:11434/api/embed";
        u = u.Trim();
        if (u.EndsWith("/")) return u + "api/embed";
        if (u.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) return u + "/embed";
        if (u.Contains("/api/embed", StringComparison.OrdinalIgnoreCase)) return u;
        if (u.Contains("/api/embeddings", StringComparison.OrdinalIgnoreCase)) return u;
        return u + "/api/embed";
    }

    private string ResolveEmbeddingUrlLegacy()
    {
        var u = _config.Url;
        if (string.IsNullOrWhiteSpace(u)) return "http://localhost:11434/api/embeddings";
        u = u.Trim();
        if (u.EndsWith("/")) return u + "api/embeddings";
        if (u.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) return u + "/embeddings";
        if (u.Contains("/api/embeddings", StringComparison.OrdinalIgnoreCase)) return u;
        return u + "/api/embeddings";
    }

    private static bool IsLikelyEmbedEndpoint(string url)
        => !string.IsNullOrWhiteSpace(url) &&
           (url.Contains("/api/embed", StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith("/api/embed", StringComparison.OrdinalIgnoreCase));
}

