using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using DText = DocumentFormat.OpenXml.Drawing.Text;
using ExcelDataReader;
using QMan.Core;
using UglyToad.PdfPig;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace QMan.Ingestion;

public sealed class DocumentParserService
{
    private const long MaxAcceptedFileBytes = 128L * 1024 * 1024;
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".pptx", ".ppt", ".docx", ".doc", ".xlsx", ".xls", ".txt",
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tiff"
    };

    static DocumentParserService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public IReadOnlyList<ParsedUnit> Parse(string filePath)
    {
        var normalizedPath = NormalizeAndValidatePath(filePath);
        var name = System.IO.Path.GetFileName(normalizedPath).ToLowerInvariant();
        try
        {
            if (name.EndsWith(".pdf", StringComparison.Ordinal)) return ParsePdf(normalizedPath);
            if (name.EndsWith(".pptx", StringComparison.Ordinal)) return ParsePptxOpenXml(normalizedPath);
            if (name.EndsWith(".ppt", StringComparison.Ordinal))
                return new List<ParsedUnit> { new(null, "(.ppt 구 형식은 DocumentFormat.OpenXml 미지원 — 변환 필요)") };
            if (name.EndsWith(".xlsx", StringComparison.Ordinal) || name.EndsWith(".xls", StringComparison.Ordinal))
                return ParseExcel(normalizedPath);
            if (name.EndsWith(".docx", StringComparison.Ordinal)) return ParseDocxOpenXml(normalizedPath);
            if (name.EndsWith(".doc", StringComparison.Ordinal))
                return new List<ParsedUnit> { new(null, "(.doc 구 형식은 이 빌드에서 미지원 — .docx 변환 권장)") };
            if (name.EndsWith(".txt", StringComparison.Ordinal)) return ParseTxt(normalizedPath);
            if (IsImage(name)) return ParseImage(normalizedPath);
            return ParseTxt(normalizedPath);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("문서 파싱 실패: " + System.IO.Path.GetFileName(normalizedPath), e);
        }
    }

    private static bool IsImage(string name) =>
        name.EndsWith(".png") || name.EndsWith(".jpg") || name.EndsWith(".jpeg") ||
        name.EndsWith(".bmp") || name.EndsWith(".gif") || name.EndsWith(".tiff");

    private static IReadOnlyList<ParsedUnit> ParseTxt(string path)
    {
        var txt = File.ReadAllText(path, Encoding.UTF8);
        return new List<ParsedUnit> { new(null, txt) };
    }

    private static IReadOnlyList<ParsedUnit> ParsePdf(string path)
    {
        var list = new List<ParsedUnit>();
        using var doc = PdfDocument.Open(path);
        var pageNo = 0;
        var hadPageFailure = false;
        foreach (var page in doc.GetPages())
        {
            pageNo++;
            try
            {
                var sb = new StringBuilder();
                foreach (var w in page.GetWords())
                    sb.Append(w.Text).Append(' ');
                var text = sb.ToString().Trim();
                if (text.Length > 0)
                    list.Add(new ParsedUnit("p." + pageNo, text));
            }
            catch
            {
                hadPageFailure = true;
            }
        }

        if (list.Count == 0 && hadPageFailure)
            throw new InvalidOperationException("PDF 페이지를 안전하게 읽지 못했습니다.");

        return list;
    }

    private static IReadOnlyList<ParsedUnit> ParseDocxOpenXml(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return new List<ParsedUnit>();
        var sb = new StringBuilder();
        foreach (var para in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            var t = para.InnerText;
            if (!string.IsNullOrWhiteSpace(t))
                sb.AppendLine(t);
        }
        var text = sb.ToString().Trim();
        return text.Length == 0
            ? new List<ParsedUnit>()
            : new List<ParsedUnit> { new(null, text) };
    }

    private static IReadOnlyList<ParsedUnit> ParseExcel(string path)
    {
        var list = new List<ParsedUnit>();
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        do
        {
            var sheetName = reader.Name ?? "sheet";
            var sb = new StringBuilder();

            while (reader.Read())
            {
                for (int col = 0; col < reader.FieldCount; col++)
                {
                    var value = reader.GetValue(col);
                    if (value != null && !Convert.IsDBNull(value))
                    {
                        var strValue = value.ToString();
                        if (!string.IsNullOrWhiteSpace(strValue))
                            sb.Append(strValue).Append(' ');
                    }
                }
                sb.AppendLine();
            }

            var text = sb.ToString().Trim();
            if (text.Length > 0)
                list.Add(new ParsedUnit("sheet: " + sheetName, text));

        } while (reader.NextResult());

        return list;
    }

    private static IReadOnlyList<ParsedUnit> ParsePptxOpenXml(string path)
    {
        var list = new List<ParsedUnit>();
        using var doc = PresentationDocument.Open(path, false);
        var presPart = doc.PresentationPart;
        if (presPart?.Presentation?.SlideIdList is null) return list;

        var idx = 0;
        foreach (var slideId in presPart.Presentation.SlideIdList!.Elements<SlideId>())
        {
            idx++;
            var relId = slideId.RelationshipId?.Value;
            if (relId is null) continue;
            var slidePart = (SlidePart)presPart.GetPartById(relId);
            if (slidePart.Slide is null) continue;
            var sb = new StringBuilder();
            foreach (var t in slidePart.Slide.Descendants<DText>())
            {
                var s = t.Text;
                if (!string.IsNullOrWhiteSpace(s))
                    sb.AppendLine(s);
            }
            var text = sb.ToString().Trim();
            if (text.Length > 0)
                list.Add(new ParsedUnit("slide " + idx, text));
        }
        return list;
    }

    private static string NormalizeAndValidatePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new InvalidOperationException("파일 경로가 비어 있습니다.");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("잘못된 파일 경로입니다.", ex);
        }

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("선택한 파일을 찾을 수 없습니다.", fullPath);

        var attrs = File.GetAttributes(fullPath);
        if ((attrs & FileAttributes.Directory) != 0)
            throw new InvalidOperationException("폴더는 업로드할 수 없습니다.");

        var ext = Path.GetExtension(fullPath);
        if (string.IsNullOrWhiteSpace(ext) || !SupportedExtensions.Contains(ext))
            throw new InvalidOperationException("지원하지 않는 파일 형식입니다.");

        var info = new FileInfo(fullPath);
        if (info.Length > MaxAcceptedFileBytes)
            throw new InvalidOperationException(
                $"보안을 위해 {MaxAcceptedFileBytes / (1024 * 1024)}MB 이하 파일만 업로드할 수 있습니다.");

        return fullPath;
    }

    private static IReadOnlyList<ParsedUnit> ParseImage(string path)
    {
        if (!OperatingSystem.IsWindows())
            return new List<ParsedUnit> { new(null, "이미지 OCR은 Windows에서만 지원됩니다.") };

        try
        {
            var text = RunWindowsOcr(path);
            if (text.Length > 0)
                return new List<ParsedUnit> { new(null, text) };
            return new List<ParsedUnit> { new(null, "이미지 파일: " + System.IO.Path.GetFileName(path) + " (텍스트 없음)") };
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("OCR", StringComparison.Ordinal))
        {
            return new List<ParsedUnit>
            {
                new(null, "이미지 파일: " + System.IO.Path.GetFileName(path) +
                          " (Windows OCR 미설치 — 설정 > 시간 및 언어 > 언어에서 한국어/영어 OCR 옵션 추가)")
            };
        }
        catch
        {
            return new List<ParsedUnit> { new(null, "이미지 파일: " + System.IO.Path.GetFileName(path) + " (OCR 실패)") };
        }
    }

    /// <summary>
    /// WinRT OCR은 UI 스레드에서 <c>GetAwaiter().GetResult()</c>로 동기 대기하면
    /// Dispatcher로의 연속 마샬링 때문에 데드락이 날 수 있어, 항상 스레드 풀에서 실행합니다.
    /// </summary>
    private static string RunWindowsOcr(string path) =>
        Task.Run(() => RunWindowsOcrAsync(path).GetAwaiter().GetResult()).GetAwaiter().GetResult();

    private static async Task<string> RunWindowsOcrAsync(string path)
    {
        await using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var ras = fileStream.AsRandomAccessStream();

        var decoder = await BitmapDecoder.CreateAsync(ras).AsTask().ConfigureAwait(false);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb).AsTask().ConfigureAwait(false);

        var engine = OcrEngine.TryCreateFromLanguage(new Language("ko"))
            ?? OcrEngine.TryCreateFromLanguage(new Language("ko-KR"))
            ?? OcrEngine.TryCreateFromUserProfileLanguages()
            ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"))
            ?? OcrEngine.TryCreateFromLanguage(new Language("en"));
        if (engine is null)
            throw new InvalidOperationException("OCR 엔진을 만들 수 없습니다. Windows에 OCR용 언어 팩이 있는지 확인하세요.");

        var result = await engine.RecognizeAsync(softwareBitmap).AsTask().ConfigureAwait(false);
        var sb = new StringBuilder();
        foreach (var line in result.Lines)
            sb.AppendLine(line.Text);
        return sb.ToString().Trim();
    }
}
