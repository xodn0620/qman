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
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO chunk_vec(rowid, embedding) VALUES ($id, $emb);";
        cmd.Parameters.AddWithValue("$id", chunkId);
        cmd.Parameters.AddWithValue("$emb", embeddingJson);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<VecHit> Knn(string queryEmbeddingJson, int topK, long? categoryId)
    {
        if (!IsEnabled) return Array.Empty<VecHit>();

        var sql = categoryId is null
            ? """
              SELECT v.rowid AS chunk_id, v.distance
              FROM chunk_vec v
              WHERE v.embedding MATCH $q
              ORDER BY v.distance
              LIMIT $k;
              """
            : """
              SELECT v.rowid AS chunk_id, v.distance
              FROM chunk_vec v
              JOIN chunks c ON c.id = v.rowid
              JOIN documents d ON d.id = c.document_id
              WHERE v.embedding MATCH $q
                AND d.category_id = $cat
              ORDER BY v.distance
              LIMIT $k;
              """;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$q", queryEmbeddingJson);
        cmd.Parameters.AddWithValue("$k", topK);
        if (categoryId is not null)
            cmd.Parameters.AddWithValue("$cat", categoryId.Value);

        var list = new List<VecHit>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new VecHit(rd.GetInt64(0), rd.GetDouble(1)));
        return list;
    }
}
