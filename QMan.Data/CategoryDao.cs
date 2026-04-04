using Microsoft.Data.Sqlite;

namespace QMan.Data;

public sealed class CategoryDao
{
    public sealed record Category(long Id, string Name, string CreatedAt, int SortOrder);

    private readonly SqliteConnection _conn;

    public CategoryDao(SqliteConnection conn) => _conn = conn;

    public IReadOnlyList<Category> ListAll()
    {
        var list = new List<Category>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, created_at, sort_order
            FROM categories
            ORDER BY sort_order ASC, id ASC;
            """;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new Category(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3)));
        return list;
    }

    public Category Create(string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO categories(name, sort_order)
            VALUES ($name, (SELECT COALESCE(MAX(sort_order), -1) + 1 FROM categories));
            SELECT id, name, created_at, sort_order FROM categories WHERE id = last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        using var rd = cmd.ExecuteReader();
        rd.Read();
        return new Category(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3));
    }

    public void SetSortOrder(IReadOnlyList<long> orderedCategoryIds)
    {
        using var tx = _conn.BeginTransaction();
        try
        {
            for (var i = 0; i < orderedCategoryIds.Count; i++)
            {
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE categories SET sort_order = $ord WHERE id = $id;";
                cmd.Parameters.AddWithValue("$ord", i);
                cmd.Parameters.AddWithValue("$id", orderedCategoryIds[i]);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
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
