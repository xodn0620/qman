using System.IO;
using System.Reflection;
using QMan.Core;

namespace QMan.App;

/// <summary>
/// 빌드 시 QMan.App\native\ 에 넣은 sqlite-vec DLL을 임베드한 뒤, 첫 실행 시 %LocalAppData%\QMan\vec\ 로 풉니다.
/// (SQLite LoadExtension은 파일 경로가 필요하므로 디스크 캐시는 필수이며, QMan.exe 옆 폴더는 비웁니다.)
/// </summary>
internal static class NativeVecBootstrap
{
    private const string ResourcePrefix = "QMan.BundledNative.";

    public static void EnsureBundledNativeExtracted()
    {
        var asm = typeof(NativeVecBootstrap).Assembly;
        foreach (var fullName in asm.GetManifestResourceNames())
        {
            if (!fullName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                continue;
            var fileName = fullName[ResourcePrefix.Length..];
            if (string.IsNullOrWhiteSpace(fileName))
                continue;
            ExtractIfNeeded(asm, fullName, fileName);
        }
    }

    private static void ExtractIfNeeded(Assembly asm, string resourceName, string fileName)
    {
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
            return;

        Directory.CreateDirectory(AppPaths.SqliteVecCacheDir);
        var dest = Path.Combine(AppPaths.SqliteVecCacheDir, fileName);
        if (File.Exists(dest))
        {
            try
            {
                if (new FileInfo(dest).Length == stream.Length)
                    return;
            }
            catch
            {
                // 덮어쓰기 시도
            }
        }

        using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.Read);
        stream.CopyTo(fs);
    }
}
