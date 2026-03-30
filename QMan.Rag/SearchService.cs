using Microsoft.Data.Sqlite;
using QMan.Data;

namespace QMan.Rag;

public sealed class SearchService
{
    public sealed record SearchHit(
        long ChunkId,
        long DocumentId,
        string DocumentName,
        string? SourceLabel,
        string Content,
        double Score);

    private readonly SqliteConnection _conn;
    private readonly VecDao _vec;

    public SearchService(SqliteDb db, VecDao vec)
    {
        _conn = db.Connection;
        _vec = vec;
    }

    public IReadOnlyList<SearchHit> Search(float[] queryEmbedding, int topK, long? categoryId)
    {
        var queryJson = EmbeddingUtil.ToJsonArray(queryEmbedding);

        if (_vec.IsEnabled)
        {
            try
            {
                var hits = _vec.Knn(queryJson, topK, categoryId);
                if (hits.Count > 0)
                    return LookupChunksForVecHits(hits);
            }
            catch
            {
                // vec 실패 → fallback
            }
        }

        return FallbackCosineSearch(queryEmbedding, topK, categoryId);
    }

    private IReadOnlyList<SearchHit> LookupChunksForVecHits(IReadOnlyList<VecDao.VecHit> vecHits)
    {
        var ids = string.Join(",", vecHits.Select(h => h.ChunkId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (ids.Length == 0) return Array.Empty<SearchHit>();

        var map = new Dictionary<long, SearchHit>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.id, c.document_id, d.original_name, c.source_label, c.content
            FROM chunks c
            JOIN documents d ON d.id = c.document_id
            WHERE c.id IN ({ids});
            """;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var chunkId = rd.GetInt64(0);
            map[chunkId] = new SearchHit(
                chunkId,
                rd.GetInt64(1),
                rd.GetString(2),
                rd.IsDBNull(3) ? null : rd.GetString(3),
                rd.GetString(4),
                0.0);
        }

        var list = new List<SearchHit>();
        foreach (var vh in vecHits)
        {
            if (map.TryGetValue(vh.ChunkId, out var hit))
                list.Add(hit with { Score = -vh.Distance });
        }

        return list;
    }

    private IReadOnlyList<SearchHit> FallbackCosineSearch(float[] queryEmbedding, int topK, long? categoryId)
    {
        var sql = categoryId is null
            ? """
              SELECT e.chunk_id, e.embedding_json, c.document_id, d.original_name, c.source_label, c.content
              FROM chunk_embeddings e
              JOIN chunks c ON c.id = e.chunk_id
              JOIN documents d ON d.id = c.document_id;
              """
            : """
              SELECT e.chunk_id, e.embedding_json, c.document_id, d.original_name, c.source_label, c.content
              FROM chunk_embeddings e
              JOIN chunks c ON c.id = e.chunk_id
              JOIN documents d ON d.id = c.document_id
              WHERE d.category_id = $cat;
              """;

        var scored = new List<SearchHit>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        if (categoryId is not null)
            cmd.Parameters.AddWithValue("$cat", categoryId.Value);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var chunkId = rd.GetInt64(0);
            var embJson = rd.GetString(1);
            var emb = EmbeddingUtil.ParseJsonArray(embJson);
            var cos = EmbeddingUtil.Cosine(queryEmbedding, emb);
            scored.Add(new SearchHit(
                chunkId,
                rd.GetInt64(2),
                rd.GetString(3),
                rd.IsDBNull(4) ? null : rd.GetString(4),
                rd.GetString(5),
                cos));
        }

        return scored
            .OrderByDescending(h => h.Score)
            .Take(topK)
            .ToList();
    }
}
