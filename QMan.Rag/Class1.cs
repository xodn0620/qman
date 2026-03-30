using System.Text.Json;

namespace QMan.Rag;

public static class EmbeddingUtil
{
    public static string ToJsonArray(float[] v)
    {
        var sb = new System.Text.StringBuilder(v.Length * 8 + 2);
        sb.Append('[');
        for (int i = 0; i < v.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(v[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }

    public static float[] ParseJsonArray(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return Array.Empty<float>();

            var result = new float[root.GetArrayLength()];
            var i = 0;
            foreach (var e in root.EnumerateArray())
                result[i++] = (float)e.GetDouble();
            return result;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("embedding_json 파싱 실패", e);
        }
    }

    public static double Cosine(float[] a, float[] b)
    {
        double dot = 0.0, na = 0.0, nb = 0.0;
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            double x = a[i];
            double y = b[i];
            dot += x * y;
            na += x * x;
            nb += y * y;
        }
        if (na == 0.0 || nb == 0.0) return 0.0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}

