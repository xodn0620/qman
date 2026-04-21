using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace QMan.Data;

/// <summary>
/// sqlite-vec vec0에 넣을 float32 리틀엔디안 BLOB (JSON 파싱/로캘 이슈 회피).
/// </summary>
public static class VecEncoding
{
    /// <summary>CRT/sqlite-vec JSON 경로용 — 항상 InvariantCulture 소수점.</summary>
    public static string ToInvariantJsonArray(float[] v)
    {
        var sb = new StringBuilder(v.Length * 8 + 2);
        sb.Append('[');
        for (var i = 0; i < v.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var x = v[i];
            if (float.IsNaN(x) || float.IsInfinity(x))
                x = 0f;
            sb.Append(x.ToString(CultureInfo.InvariantCulture));
        }

        sb.Append(']');
        return sb.ToString();
    }

    public static byte[] FloatsToLittleEndianBlob(float[] v)
    {
        var b = new byte[v.Length * 4];
        for (var i = 0; i < v.Length; i++)
        {
            var x = v[i];
            if (float.IsNaN(x) || float.IsInfinity(x))
                x = 0f;
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(i * 4), x);
        }

        return b;
    }
}
