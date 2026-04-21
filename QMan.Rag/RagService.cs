using System.Text.RegularExpressions;
using QMan.Core;
using QMan.Data;
using QMan.Llm;

namespace QMan.Rag;

public sealed class RagService
{
    private readonly AppConfig _cfg;
    private readonly SqliteDb _db;
    private readonly ILlmClient _llm;
    private readonly SearchService _search;
    private readonly EmbeddingDao _embeddingDao;
    private readonly VecDao _vecDao;

    public RagService(
        AppConfig cfg,
        SqliteDb db,
        ILlmClient llm,
        SearchService search,
        EmbeddingDao embeddingDao,
        VecDao vecDao)
    {
        _cfg = cfg;
        _db = db;
        _llm = llm;
        _search = search;
        _embeddingDao = embeddingDao;
        _vecDao = vecDao;
    }

    public async Task<string> AnswerAsync(string question, long? categoryId, CancellationToken ct = default)
    {
        var qEmb = await _llm.EmbedAsync(question, ct).ConfigureAwait(false);
        var hits = _search.Search(qEmb, 6, categoryId);

        var ctx = new System.Text.StringBuilder();
        var topDocNames = new List<string>();

        var i = 0;
        foreach (var h in hits)
        {
            i++;
            ctx.Append('[').Append(i).Append("] 문서: ").Append(h.DocumentName);
            if (!string.IsNullOrWhiteSpace(h.SourceLabel))
                ctx.Append(" (").Append(h.SourceLabel).Append(')');
            ctx.AppendLine();
            ctx.AppendLine(h.Content);
            ctx.AppendLine();

            if (i <= 3)
                topDocNames.Add(h.DocumentName);
        }

        const string system = """
            당신은 사내 매뉴얼 질의응답 도우미입니다.
            일반적인 존댓말(해요체)로 자연스럽게 답변하세요. 반말은 사용하지 마세요.
            제공된 '근거' 안에서만 답변하고, 근거가 부족하면 모른다고 말하세요.

            중요: 답변 작성 시 실제로 참고한 문서 번호를 답변 마지막 줄에 다음 형식으로 표시하세요:
            [SOURCES: 1, 3, 5]

            답변 본문에는 인용 표기([1], [2]...)를 포함하지 마세요.
            """;

        var user = $"""
            질문:
            {question}

            근거:
            {(ctx.Length == 0 ? "(근거 없음)" : ctx.ToString())}
            """;

        var raw = (await _llm.ChatAsync(system, user, ct).ConfigureAwait(false)).Trim();

        var usedDocuments = new List<string>();
        var pattern = new Regex(@"\[SOURCES:\s*([\d,\s]+)\]", RegexOptions.IgnoreCase);
        var m = pattern.Match(raw);
        if (m.Success)
        {
            foreach (var part in m.Groups[1].Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out var num) && num >= 1 && num <= hits.Count)
                    usedDocuments.Add(hits[num - 1].DocumentName);
            }

            raw = pattern.Replace(raw, "").Trim();
        }

        var answer = StripTrailingCitations(raw);

        if (usedDocuments.Count > 0)
            answer += "\n\n[ 참조문서: " + string.Join(", ", usedDocuments.Distinct()) + " ]";
        else if (topDocNames.Count > 0)
            answer += "\n\n[ 참조문서: " + string.Join(", ", topDocNames.Take(3)) + " ]";

        return answer;
    }

    public Task<string> AnswerAsync(string question, CancellationToken ct = default)
        => AnswerAsync(question, null, ct);

    public void IndexChunkEmbedding(long chunkId, float[] embedding)
    {
        var json = EmbeddingUtil.ToJsonArray(embedding);
        _embeddingDao.Upsert(chunkId, _cfg.EmbeddingModel, embedding.Length, json);
        if (_db.VecEnabled && embedding.Length > 0)
        {
            try
            {
                _db.EnsureVecTableDim(embedding.Length);
                _vecDao.Upsert(chunkId, embedding);
            }
            catch (Exception ex)
            {
                _db.DisableVec(ex.Message);
            }
        }
    }

    private static string StripTrailingCitations(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return Regex.Replace(s, @"(\s*\[\d+\]\s*)+$", "").Trim();
    }
}
