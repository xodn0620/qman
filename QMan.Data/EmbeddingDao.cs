using Microsoft.Data.Sqlite;

namespace QMan.Data;

public sealed class EmbeddingDao
{
    private readonly SqliteConnection _conn;

    public EmbeddingDao(SqliteConnection conn) => _conn = conn;

    public void Upsert(long chunkId, string model, int dim, string embeddingJson)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chunk_embeddings(chunk_id, embedding_model, embedding_dim, embedding_json)
            VALUES ($id, $model, $dim, $json)
            ON CONFLICT(chunk_id) DO UPDATE SET
              embedding_model = excluded.embedding_model,
              embedding_dim   = excluded.embedding_dim,
              embedding_json  = excluded.embedding_json;
            """;
        cmd.Parameters.AddWithValue("$id", chunkId);
        cmd.Parameters.AddWithValue("$model", model);
        cmd.Parameters.AddWithValue("$dim", dim);
        cmd.Parameters.AddWithValue("$json", embeddingJson);
        cmd.ExecuteNonQuery();
    }
}
