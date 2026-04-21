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

    /// <summary>
    /// sqlite-vec: vec_f32(BLOB) 우선. 일부 빌드/모델 조합에서만 SQL logic error가 나면
    /// Invariant JSON 텍스트 또는 vec_f32(JSON)로 재시도한다.
    /// </summary>
    public void Upsert(long chunkId, float[] embedding)
    {
        if (!IsEnabled || embedding.Length == 0) return;
        var blob = VecEncoding.FloatsToLittleEndianBlob(embedding);
        var json = VecEncoding.ToInvariantJsonArray(embedding);
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

            InsertChunkVecOrThrow(tx, chunkId, blob, json);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void InsertChunkVecOrThrow(SqliteTransaction tx, long chunkId, byte[] blob, string json)
    {
        Exception? last = null;
        var attempts = new (string Sql, Action<SqliteCommand> Bind)[]
        {
            (
                "INSERT INTO chunk_vec(rowid, embedding) VALUES ($rid, vec_f32($emb));",
                c =>
                {
                    c.Parameters.AddWithValue("$rid", chunkId);
                    c.Parameters.AddWithValue("$emb", blob);
                }
            ),
            (
                "INSERT INTO chunk_vec(rowid, embedding) VALUES ($rid, $json);",
                c =>
                {
                    c.Parameters.AddWithValue("$rid", chunkId);
                    c.Parameters.AddWithValue("$json", json);
                }
            ),
            (
                "INSERT INTO chunk_vec(rowid, embedding) VALUES ($rid, vec_f32($json));",
                c =>
                {
                    c.Parameters.AddWithValue("$rid", chunkId);
                    c.Parameters.AddWithValue("$json", json);
                }
            )
        };

        foreach (var (sql, bind) in attempts)
        {
            try
            {
                using var ins = tx.Connection!.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = sql;
                bind(ins);
                ins.ExecuteNonQuery();
                return;
            }
            catch (SqliteException ex)
            {
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException("chunk_vec INSERT 실패");
    }

    public IReadOnlyList<VecHit> Knn(float[] queryEmbedding, int topK, long? categoryId)
    {
        if (!IsEnabled || queryEmbedding.Length == 0) return Array.Empty<VecHit>();

        try
        {
            return KnnCore(queryEmbedding, topK, categoryId);
        }
        catch (SqliteException)
        {
            // sqlite-vec + JOIN/MATCH 조합 등 환경별 SQL logic error → 상위에서 코사인 폴백
            return Array.Empty<VecHit>();
        }
    }

    private IReadOnlyList<VecHit> KnnCore(float[] queryEmbedding, int topK, long? categoryId)
    {
        // MATCH는 단순 FROM chunk_vec만 사용 (일부 sqlite-vec 빌드에서 JOIN+MATCH가 SQL logic error 유발)
        var fetchLimit = categoryId is null
            ? topK
            : Math.Max(topK * 40, Math.Min(500, topK * 100));

        var qBlob = VecEncoding.FloatsToLittleEndianBlob(queryEmbedding);
        var qJson = VecEncoding.ToInvariantJsonArray(queryEmbedding);

        List<VecHit> candidates;
        try
        {
            candidates = ExecuteKnnQuery("""
                SELECT v.rowid AS chunk_id, v.distance
                FROM chunk_vec v
                WHERE v.embedding MATCH vec_f32($q)
                ORDER BY v.distance
                LIMIT $k;
                """, c =>
            {
                c.Parameters.AddWithValue("$q", qBlob);
                c.Parameters.AddWithValue("$k", fetchLimit);
            });
        }
        catch (SqliteException)
        {
            candidates = ExecuteKnnQuery("""
                SELECT v.rowid AS chunk_id, v.distance
                FROM chunk_vec v
                WHERE v.embedding MATCH $qj
                ORDER BY v.distance
                LIMIT $k;
                """, c =>
            {
                c.Parameters.AddWithValue("$qj", qJson);
                c.Parameters.AddWithValue("$k", fetchLimit);
            });
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

    private List<VecHit> ExecuteKnnQuery(string sql, Action<SqliteCommand> bind)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        bind(cmd);
        var list = new List<VecHit>();
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
                list.Add(new VecHit(rd.GetInt64(0), rd.GetDouble(1)));
        }

        return list;
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
