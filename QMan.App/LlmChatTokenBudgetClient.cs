using System.Windows;
using QMan.Core;
using QMan.Llm;

namespace QMan.App;

/// <summary>
/// 채팅 한 번의 요청+응답 합이 <see cref="LlmChatContextLimit.MaxCombinedTokens"/> 을 넘지 않도록 추정 검사.
/// </summary>
public sealed class LlmChatTokenBudgetClient : ILlmClient
{
    private readonly ILlmClient _inner;

    public LlmChatTokenBudgetClient(ILlmClient inner) => _inner = inner;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
        _inner.EmbedAsync(text, ct);

    public async Task<string> ChatAsync(string system, string user, CancellationToken ct = default)
    {
        var inTok = LlmChatContextLimit.EstimateChatRequestTokens(system, user);
        if (inTok + LlmChatContextLimit.ReservedOutputTokens > LlmChatContextLimit.MaxCombinedTokens)
        {
            RunOnUi(() =>
            {
                MessageBox.Show(
                    Application.Current?.MainWindow,
                    "사내 LLM 규정으로, 한 번의 요청에서 요청·응답 토큰 합계는 " +
                    $"{LlmChatContextLimit.MaxCombinedTokens:N0}을 넘을 수 없습니다.\n\n" +
                    $"추정 요청 토큰: 약 {inTok:N0}\n" +
                    $"(응답 여유 {LlmChatContextLimit.ReservedOutputTokens:N0} 토큰을 포함하면 상한을 초과합니다.)\n\n" +
                    "질문을 줄이거나, 검색 근거가 줄어들도록 카테고리를 좁혀 보세요.",
                    "토큰 한도 초과",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
            throw new InvalidOperationException(
                $"요청 토큰(추정 약 {inTok:N0})이 한도 내 응답 여유를 포함해 허용 범위를 초과했습니다.");
        }

        var answer = await _inner.ChatAsync(system, user, ct).ConfigureAwait(false);
        var outTok = LlmChatContextLimit.EstimateTokens(answer);
        var sum = inTok + outTok;
        if (sum > LlmChatContextLimit.MaxCombinedTokens)
        {
            RunOnUi(() =>
            {
                MessageBox.Show(
                    Application.Current?.MainWindow,
                    "사내 LLM 규정상 요청·응답 토큰 합계는 " +
                    $"{LlmChatContextLimit.MaxCombinedTokens:N0}을 넘을 수 없습니다.\n\n" +
                    $"이번 호출 추정: 요청 약 {inTok:N0} + 응답 약 {outTok:N0} = 합계 약 {sum:N0} 토큰\n\n" +
                    "응답은 이미 수신되었습니다. 이후 질문은 더 짧게 나누어 주세요.",
                    "토큰 한도 초과(사후)",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }

        return answer;
    }

    private static void RunOnUi(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null)
        {
            action();
            return;
        }

        if (d.CheckAccess())
            action();
        else
            d.Invoke(action);
    }
}
