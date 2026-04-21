using System.Runtime.InteropServices;

namespace QMan.App;

/// <summary>
/// sqlite-vec가 JSON/실수 파싱에 CRT strtod(로캘 의존)를 쓰므로, 프로세스 LC_NUMERIC을 C로 고정합니다.
/// (OpenAI 등 고차원 임베딩 삽입·MATCH 시 SQL logic error 완화)
/// </summary>
internal static class CrtNumericLocale
{
    private const int LC_NUMERIC = 4;

    [DllImport("api-ms-win-crt-locale-l1-1-0.dll", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi, EntryPoint = "setlocale", ExactSpelling = false)]
    private static extern nint SetLocale(int category,
        [MarshalAs(UnmanagedType.LPStr)] string locale);

    internal static void TrySetNumericC()
    {
        try
        {
            SetLocale(LC_NUMERIC, "C");
        }
        catch
        {
            /* ignore */
        }
    }
}
