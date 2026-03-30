using Microsoft.Data.Sqlite;

namespace QMan.Data;

public sealed class CategoryDao
{
    public sealed record Category(long Id, string Name, string CreatedAt);

    private readonly SqliteConnection _conn;

    public CategoryDao(SqliteConnection conn) => _conn = conn;

    public IReadOnlyList<Category> ListAll()
    {
        var list = new List<Category>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, created_at
            FROM categories
            ORDER BY created_at ASC, id ASC;
            """;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new Category(rd.GetInt64(0), rd.GetString(1), rd.GetString(2)));
        return list;
    }

    public Category Create(string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO categories(name) VALUES ($name);
            SELECT id, name, created_at FROM categories WHERE id = last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        using var rd = cmd.ExecuteReader();
        rd.Read();
        return new Category(rd.GetInt64(0), rd.GetString(1), rd.GetString(2));
    }

    public void Rename(long id, string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE categories SET name = $name WHERE id = $id;";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM categories WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
