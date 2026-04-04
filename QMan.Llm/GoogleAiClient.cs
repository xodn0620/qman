using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using QMan.Core;

namespace QMan.Llm;

/// <summary>Google AI (Gemini) REST: generateContent, embedContent.</summary>
public sealed class GoogleAiClient : ILlmClient
{
    private readonly AppConfig _config;
    private readonly HttpClient _http;

    public GoogleAiClient(AppConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        RequireKey();
        // text-embedding-004 등 레거시 ID는 v1beta embedContent에서 제거됨 → gemini-embedding-001 사용.
        var model = NormalizeEmbeddingModelId(_config.EmbeddingModel);
        var url =
            $"{BaseBetaUrl()}/models/{Uri.EscapeDataString(model)}:embedContent?key={Uri.EscapeDataString(_config.OpenAiApiKey!)}";

        var body = new GoogleEmbedRequest
        {
            Model = $"models/{model}",
            Content = new GoogleEmbedContent
            {
                Parts = [new GoogleEmbedPart { Text = text ?? string.Empty }]
            },
            OutputDimensionality = 768
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw GoogleApiHttpException("임베딩", resp.StatusCode, respBody);

        using var doc = JsonDocument.Parse(respBody);
        var root = doc.RootElement;
        if (!root.TryGetProperty("embedding", out var emb))
            throw new InvalidOperationException("Google AI 임베딩 응답 파싱 실패: " + respBody);

        var values = emb.TryGetProperty("values", out var arr) ? arr : emb;
        if (values.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Google AI 임베딩 응답 파싱 실패: " + respBody);

        var v = new float[values.GetArrayLength()];
        var i = 0;
        foreach (var e in values.EnumerateArray())
            v[i++] = (float)e.GetDouble();
        return v;
    }

    public async Task<string> ChatAsync(string system, string user, CancellationToken ct = default)
    {
        RequireKey();
        var model = NormalizeChatModelId(_config.ChatModel);
        var url =
            $"{BaseBetaUrl()}/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(_config.OpenAiApiKey!)}";

        object body;
        if (string.IsNullOrWhiteSpace(system))
        {
            body = new
            {
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = user ?? string.Empty } } }
                }
            };
        }
        else
        {
            body = new
            {
                systemInstruction = new { parts = new[] { new { text = system } } },
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = user ?? string.Empty } } }
                }
            };
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw GoogleApiHttpException("채팅", resp.StatusCode, respBody);

        using var doc = JsonDocument.Parse(respBody);
        var root = doc.RootElement;
        if (!root.TryGetProperty("candidates", out var cand) || cand.ValueKind != JsonValueKind.Array ||
            cand.GetArrayLength() == 0)
        {
            var hint = root.TryGetProperty("promptFeedback", out var fb) ? fb.ToString() : respBody;
            throw new InvalidOperationException("Google AI 응답에 후보 텍스트가 없습니다. " + hint);
        }

        var first = cand[0];
        if (!first.TryGetProperty("content", out var content))
            throw new InvalidOperationException("Google AI 응답 파싱 실패: " + respBody);

        if (!content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Google AI 응답 파싱 실패: " + respBody);

        var sb = new System.Text.StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var t))
                sb.Append(t.GetString());
        }

        return sb.ToString();
    }

    private void RequireKey()
    {
        if (string.IsNullOrWhiteSpace(_config.OpenAiApiKey))
            throw new InvalidOperationException(
                "API 키가 설정되지 않았습니다. 설정에서 API 키를 입력하거나 환경변수 GEMINI_API_KEY / GOOGLE_API_KEY를 설정해 주세요.");
    }

    private string BaseBetaUrl()
    {
        var u = _config.Url?.Trim();
        if (string.IsNullOrWhiteSpace(u))
            return "https://generativelanguage.googleapis.com/v1beta";
        return u.TrimEnd('/');
    }

    private static string NormalizeChatModelId(string? model)
    {
        var m = (model ?? string.Empty).Trim();
        if (m.StartsWith("models/", StringComparison.Ordinal))
            m = m["models/".Length..];
        if (string.IsNullOrEmpty(m))
            return "gemini-2.5-flash-lite";
        // 2.0 Flash 계열은 단계적 종료·무료 할당 0(429) 사례가 많아 2.5 Flash-Lite로 치환.
        if (m.StartsWith("gemini-2.0-flash", StringComparison.OrdinalIgnoreCase))
            return "gemini-2.5-flash-lite";
        return m;
    }

    private static InvalidOperationException GoogleApiHttpException(string operationKo, HttpStatusCode status, string respBody)
    {
        if (status == (HttpStatusCode)429)
        {
            var retrySec = TryExtractRetrySecondsFromGoogleError(respBody);
            var retryHint = retrySec > 0
                ? $" 서버 안내에 따르면 약 {retrySec}초 뒤 다시 시도할 수 있습니다."
                : "";
            return new InvalidOperationException(
                $"Google AI {operationKo}: API 요청 한도(할당량)에 걸렸습니다.{retryHint} " +
                "Google AI Studio에서 프로젝트·결제(유료 플랜)와 할당량(https://ai.google.dev/gemini-api/docs/rate-limits)을 확인해 주세요. " +
                "설정의 LLM 모델이 gemini-2.0-flash 라면 종료 예정 모델이라 한도가 0으로 나올 수 있어, gemini-2.5-flash-lite 또는 gemini-2.5-flash 로 바꿔 보세요.");
        }

        var shortBody = respBody.Length > 800 ? respBody[..800] + "…" : respBody;
        return new InvalidOperationException($"Google AI {operationKo} 실패: HTTP {(int)status} / {shortBody}");
    }

    private static int TryExtractRetrySecondsFromGoogleError(string respBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(respBody);
            if (!doc.RootElement.TryGetProperty("error", out var err))
                return 0;
            if (err.TryGetProperty("message", out var msg))
            {
                var s = msg.GetString() ?? "";
                var m = Regex.Match(s, @"retry in\s+([\d.]+)\s*s", RegexOptions.IgnoreCase);
                if (m.Success && double.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out var sec))
                    return (int)Math.Ceiling(sec);
            }

            if (err.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    if (!d.TryGetProperty("@type", out var t) ||
                        t.GetString() != "type.googleapis.com/google.rpc.RetryInfo")
                        continue;
                    if (!d.TryGetProperty("retryDelay", out var rd))
                        continue;
                    var delay = rd.GetString();
                    if (string.IsNullOrEmpty(delay))
                        continue;
                    if (delay.EndsWith("s", StringComparison.Ordinal) &&
                        int.TryParse(delay.AsSpan(0, delay.Length - 1), NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out var whole))
                        return whole;
                }
            }
        }
        catch
        {
            // ignore
        }

        return 0;
    }

    private static string NormalizeEmbeddingModelId(string? model)
    {
        var m = (model ?? string.Empty).Trim();
        if (m.StartsWith("models/", StringComparison.Ordinal))
            m = m["models/".Length..];
        if (string.IsNullOrEmpty(m))
            return "gemini-embedding-001";
        if (string.Equals(m, "text-embedding-004", StringComparison.OrdinalIgnoreCase))
            return "gemini-embedding-001";
        return m;
    }

    private sealed class GoogleEmbedRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = "";

        [JsonPropertyName("content")]
        public GoogleEmbedContent Content { get; init; } = null!;

        [JsonPropertyName("output_dimensionality")]
        public int OutputDimensionality { get; init; } = 768;
    }

    private sealed class GoogleEmbedContent
    {
        [JsonPropertyName("parts")]
        public GoogleEmbedPart[] Parts { get; init; } = [];
    }

    private sealed class GoogleEmbedPart
    {
        [JsonPropertyName("text")]
        public string Text { get; init; } = "";
    }

}
