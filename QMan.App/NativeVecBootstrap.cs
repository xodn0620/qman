using System.IO;
using System.Reflection;
using QMan.Core;
using System.Security.Cryptography;

namespace QMan.App;

/// <summary>
/// 빌드 시 QMan.App\native\ 에 넣은 sqlite-vec DLL을 임베드한 뒤, 첫 실행(또는 누락 시) <see cref="AppPaths.NativeDir"/> 로 풉니다.
/// (SQLite LoadExtension은 파일 경로가 필요합니다. <c>data\</c>와 같이 exe 옆에 <c>native\</c> 를 둡니다.)
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

        Directory.CreateDirectory(AppPaths.NativeDir);
        var dest = Path.Combine(AppPaths.NativeDir, fileName);
        try
        {
            // Read resource into memory to compute hash and then write atomically.
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();

            // If file exists and length matches and sidecar hash matches, skip extraction.
            var sidecar = dest + ".sha256";
            if (File.Exists(dest) && new FileInfo(dest).Length == bytes.Length && File.Exists(sidecar))
            {
                try
                {
                    var existingHashHex = File.ReadAllText(sidecar).Trim();
                    var actualHash = SHA256.HashData(bytes);
                    var actualHex = BitConverter.ToString(actualHash).Replace("-", "").ToLowerInvariant();
                    if (string.Equals(existingHashHex, actualHex, StringComparison.OrdinalIgnoreCase))
                        return;
                }
                catch
                {
                    // If sidecar read/parse fails, fall through to overwrite.
                }
            }

            // Write file and sidecar atomically.
            var tmp = dest + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            var sha = SHA256.HashData(bytes);
            var hex = BitConverter.ToString(sha).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(tmp + ".sha256", hex);
            if (File.Exists(dest)) File.Delete(dest);
            if (File.Exists(dest + ".sha256")) File.Delete(dest + ".sha256");
            File.Move(tmp, dest);
            File.Move(tmp + ".sha256", dest + ".sha256");
        }
        catch
        {
            // 덮어쓰기 시도 실패하면 무시하고 기존 동작을 유지 (로딩 실패는 별도 처리)
        }
    }
}
