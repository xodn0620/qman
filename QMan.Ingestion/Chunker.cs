namespace QMan.Ingestion;

public sealed class Chunker
{
    private readonly int _chunkChars;
    private readonly int _overlapChars;

    public Chunker(int chunkChars, int overlapChars)
    {
        _chunkChars = Math.Max(200, chunkChars);
        _overlapChars = Math.Max(0, Math.Min(overlapChars, _chunkChars / 2));
    }

    public List<string> Chunk(string text)
    {
        var cleaned = Normalize(text);
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(cleaned)) return result;

        var start = 0;
        while (start < cleaned.Length)
        {
            var end = Math.Min(cleaned.Length, start + _chunkChars);
            var part = cleaned[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(part))
                result.Add(part);

            if (end == cleaned.Length) break;
            start = Math.Max(0, end - _overlapChars);
        }

        return result;
    }

    private static string Normalize(string? s)
    {
        if (s is null) return string.Empty;
        var x = s.Replace('\0', ' ');
        x = System.Text.RegularExpressions.Regex.Replace(x, "[\\t\\r]+", " ");
        x = System.Text.RegularExpressions.Regex.Replace(x, " +", " ");
        return x.Trim();
    }
}
