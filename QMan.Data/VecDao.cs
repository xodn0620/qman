using Microsoft.Data.Sqlite;

namespace QMan.Data;

public sealed class VecDao
{
    public sealed record VecHit(long ChunkId, double Distance);

    private readonly SqliteDb _db;
    private readonly SqliteConnection _conn;

    public VecDao(SqliteDb db)
    {
        _db = db;
        _conn = db.Connection;
    }

    public bool IsEnabled => _db.VecEnabled;

    public void Upsert(long chunkId, string embeddingJson)
    {
        if (!IsEnabled) return;
        // vec0 + INSERT OR REPLACE 는 그림자 rowids 테이블에서 UNIQUE/준비 오류를 유발하는 사례가 있어
        // DELETE 후 INSERT 로 통일 (https://github.com/asg017/sqlite-vec/issues/259 등).
        using var tx = _conn.BeginTransaction();
        try
        {
            using (var del = _conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM chunk_vec WHERE rowid = $id;";
                del.Parameters.AddWithValue("$id", chunkId);
                del.ExecuteNonQuery();
            }

            using (var ins = _conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO chunk_vec(rowid, embedding) VALUES ($id, $emb);";
                ins.Parameters.AddWithValue("$id", chunkId);
                ins.Parameters.AddWithValue("$emb", embeddingJson);
                ins.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public IReadOnlyList<VecHit> Knn(string queryEmbeddingJson, int topK, long? categoryId)
    {
        if (!IsEnabled) return Array.Empty<VecHit>();

        try
        {
            return KnnCore(queryEmbeddingJson, topK, categoryId);
        }
        catch (SqliteException)
        {
            // sqlite-vec + JOIN/MATCH 조합 등 환경별 SQL logic error → 상위에서 코사인 폴백
            return Array.Empty<VecHit>();
        }
    }

    private IReadOnlyList<VecHit> KnnCore(string queryEmbeddingJson, int topK, long? categoryId)
    {
        // MATCH는 단순 FROM chunk_vec만 사용 (일부 sqlite-vec 빌드에서 JOIN+MATCH가 SQL logic error 유발)
        var fetchLimit = categoryId is null
            ? topK
            : Math.Max(topK * 40, Math.Min(500, topK * 100));

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.rowid AS chunk_id, v.distance
            FROM chunk_vec v
            WHERE v.embedding MATCH $q
            ORDER BY v.distance
            LIMIT $k;
            """;
        cmd.Parameters.AddWithValue("$q", queryEmbeddingJson);
        cmd.Parameters.AddWithValue("$k", fetchLimit);

        var candidates = new List<VecHit>(fetchLimit);
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
                candidates.Add(new VecHit(rd.GetInt64(0), rd.GetDouble(1)));
        }

        if (categoryId is null || candidates.Count == 0)
            return candidates.Count <= topK ? candidates : candidates.Take(topK).ToList();

        var allowed = LoadChunkIdsInCategory(candidates.Select(c => c.ChunkId), categoryId.Value);
        var filtered = new List<VecHit>(topK);
        foreach (var h in candidates)
        {
            if (!allowed.Contains(h.ChunkId))
                continue;
            filtered.Add(h);
            if (filtered.Count >= topK)
                break;
        }

        return filtered;
    }

    private HashSet<long> LoadChunkIdsInCategory(IEnumerable<long> chunkIds, long categoryId)
    {
        var ids = chunkIds.Distinct().ToArray();
        var set = new HashSet<long>();
        if (ids.Length == 0)
            return set;

        const int batch = 200;
        for (var off = 0; off < ids.Length; off += batch)
        {
            var slice = ids.AsSpan(off, Math.Min(batch, ids.Length - off));
            var placeholders = string.Join(",", slice.ToArray().Select((_, i) => "$p" + i));
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT c.id
                FROM chunks c
                JOIN documents d ON d.id = c.document_id
                WHERE c.id IN ({placeholders})
                  AND d.category_id = $cat;
                """;
            for (var i = 0; i < slice.Length; i++)
                cmd.Parameters.AddWithValue("$p" + i, slice[i]);
            cmd.Parameters.AddWithValue("$cat", categoryId);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                set.Add(rd.GetInt64(0));
        }

        return set;
    }
}
