using Microsoft.Data.Sqlite;

namespace QMan.Data;

public sealed class ChunkDao
{
    public sealed record Chunk(long Id, long DocumentId, int Index, string? SourceLabel, string Content);

    private readonly SqliteConnection _conn;

    public ChunkDao(SqliteConnection conn) => _conn = conn;

    public Chunk Insert(long documentId, int index, string? sourceLabel, string content)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chunks(document_id, chunk_index, source_label, content)
            VALUES ($doc, $idx, $label, $content);
            SELECT id, document_id, chunk_index, source_label, content
            FROM chunks WHERE id = last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$doc", documentId);
        cmd.Parameters.AddWithValue("$idx", index);
        cmd.Parameters.AddWithValue("$label", sourceLabel is null ? DBNull.Value : sourceLabel);
        cmd.Parameters.AddWithValue("$content", content);
        using var rd = cmd.ExecuteReader();
        rd.Read();
        return new Chunk(
            rd.GetInt64(0),
            rd.GetInt64(1),
            rd.GetInt32(2),
            rd.IsDBNull(3) ? null : rd.GetString(3),
            rd.GetString(4));
    }

    public Chunk FindById(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, document_id, chunk_index, source_label, content
            FROM chunks WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
            throw new InvalidOperationException($"chunk {id} not found");
        return new Chunk(
            rd.GetInt64(0),
            rd.GetInt64(1),
            rd.GetInt32(2),
            rd.IsDBNull(3) ? null : rd.GetString(3),
            rd.GetString(4));
    }
}
