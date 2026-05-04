using System.Text;

namespace QMan.Core;

/// <summary>사내 LLM 등: 한 번의 채팅 요청에서 요청+응답 토큰 합 상한(추정 기준).</summary>
public static class LlmChatContextLimit
{
    /// <summary>요청+응답 합계 상한(토큰, 추정).</summary>
    public const int MaxCombinedTokens = 60_000;

    /// <summary>응답 길이를 모르므로, 사전 차단 시 예약해 둘 최대 응답 토큰(추정).</summary>
    public const int ReservedOutputTokens = 16_384;

    /// <summary>JSON role 등 포맷에 대한 대략적 오버헤드(토큰, 추정).</summary>
    private const int RequestFormatOverhead = 64;

    /// <summary>
    /// UTF-8 바이트 기반 보수적 추정(한글·혼합 텍스트에서 실제보다 과대 추정되는 편이 안전).
    /// </summary>
    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        var bytes = Encoding.UTF8.GetByteCount(text);
        return Math.Max(1, (int)Math.Ceiling(bytes / 3.0));
    }

    /// <summary>system + user 메시지에 대한 추정 입력 토큰.</summary>
    public static int EstimateChatRequestTokens(string? system, string? user) =>
        EstimateTokens(system) + EstimateTokens(user) + RequestFormatOverhead;
}
