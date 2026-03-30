using Microsoft.Data.Sqlite;

namespace QMan.Data;

public sealed class DocumentDao
{
    public sealed record Document(
        long Id,
        long? CategoryId,
        string OriginalName,
        string StoredPath,
        string UploadedAt,
        long? SizeBytes);

    private readonly SqliteConnection _conn;

    public DocumentDao(SqliteConnection conn) => _conn = conn;

    public Document Create(long? categoryId, string originalName, string storedPath, long? sizeBytes)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO documents(category_id, original_name, stored_path, size_bytes)
            VALUES ($cat, $name, $path, $size);
            SELECT id, category_id, original_name, stored_path, uploaded_at, size_bytes
            FROM documents WHERE id = last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$cat", categoryId.HasValue ? categoryId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$name", originalName);
        cmd.Parameters.AddWithValue("$path", storedPath);
        cmd.Parameters.AddWithValue("$size", sizeBytes.HasValue ? sizeBytes.Value : DBNull.Value);
        using var rd = cmd.ExecuteReader();
        rd.Read();
        return Map(rd);
    }

    public IReadOnlyList<Document> ListAll(long categoryId)
    {
        var list = new List<Document>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, category_id, original_name, stored_path, uploaded_at, size_bytes
            FROM documents
            WHERE category_id = $cat
            ORDER BY uploaded_at DESC, id DESC;
            """;
        cmd.Parameters.AddWithValue("$cat", categoryId);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(Map(rd));
        return list;
    }

    public void Delete(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM documents WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static Document Map(SqliteDataReader rd)
    {
        return new Document(
            rd.GetInt64(0),
            rd.IsDBNull(1) ? null : rd.GetInt64(1),
            rd.GetString(2),
            rd.GetString(3),
            rd.GetString(4),
            rd.IsDBNull(5) ? null : rd.GetInt64(5));
    }
}
