using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using DText = DocumentFormat.OpenXml.Drawing.Text;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using QMan.Core;
using Tesseract;
using UglyToad.PdfPig;

namespace QMan.Ingestion;

public sealed class DocumentParserService
{
    public IReadOnlyList<ParsedUnit> Parse(string filePath)
    {
        var name = System.IO.Path.GetFileName(filePath).ToLowerInvariant();
        try
        {
            if (name.EndsWith(".pdf", StringComparison.Ordinal)) return ParsePdf(filePath);
            if (name.EndsWith(".pptx", StringComparison.Ordinal)) return ParsePptxOpenXml(filePath);
            if (name.EndsWith(".ppt", StringComparison.Ordinal))
                return new List<ParsedUnit> { new(null, "(.ppt 구 형식은 DocumentFormat.OpenXml 미지원 — NPOI 확장 또는 변환 필요)") };
            if (name.EndsWith(".xlsx", StringComparison.Ordinal)) return ParseXlsxOpenXml(filePath);
            if (name.EndsWith(".xls", StringComparison.Ordinal)) return ParseXls(filePath);
            if (name.EndsWith(".docx", StringComparison.Ordinal)) return ParseDocxOpenXml(filePath);
            if (name.EndsWith(".doc", StringComparison.Ordinal))
                return new List<ParsedUnit> { new(null, "(.doc 구 형식은 이 빌드에서 미지원 — .docx 변환 권장)") };
            if (name.EndsWith(".txt", StringComparison.Ordinal)) return ParseTxt(filePath);
            if (IsImage(name)) return ParseImage(filePath);
            return ParseTxt(filePath);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("문서 파싱 실패: " + filePath, e);
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
        foreach (var page in doc.GetPages())
        {
            pageNo++;
            var sb = new StringBuilder();
            foreach (var w in page.GetWords())
                sb.Append(w.Text).Append(' ');
            var text = sb.ToString().Trim();
            if (text.Length > 0)
                list.Add(new ParsedUnit("p." + pageNo, text));
        }
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

    private static IReadOnlyList<ParsedUnit> ParseXlsxOpenXml(string path)
    {
        var list = new List<ParsedUnit>();
        using var doc = SpreadsheetDocument.Open(path, false);
        var wbPart = doc.WorkbookPart;
        if (wbPart is null) return list;

        foreach (var sheet in wbPart.Workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>())
        {
            var name = sheet.Name?.Value ?? "sheet";
            var id = sheet.Id?.Value;
            if (id is null) continue;
            var wsPart = (WorksheetPart)wbPart.GetPartById(id);
            var sb = new StringBuilder();
            var sheetData = wsPart.Worksheet.GetFirstChild<SheetData>();
            if (sheetData is null) continue;
            foreach (var row in sheetData.Elements<Row>())
            {
                foreach (var cell in row.Elements<Cell>())
                {
                    var v = GetOpenXmlCellString(cell, wbPart);
                    if (!string.IsNullOrWhiteSpace(v))
                        sb.Append(v).Append(' ');
                }
                sb.AppendLine();
            }
            var text = sb.ToString().Trim();
            if (text.Length > 0)
                list.Add(new ParsedUnit("sheet: " + name, text));
        }
        return list;
    }

    private static string? GetOpenXmlCellString(Cell cell, WorkbookPart wbPart)
    {
        if (cell.DataType is not null && cell.DataType == CellValues.SharedString)
        {
            var si = int.Parse(cell.InnerText, System.Globalization.CultureInfo.InvariantCulture);
            var items = wbPart.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>();
            if (items is null) return null;
            var item = items.ElementAtOrDefault(si);
            return item?.InnerText;
        }
        return cell.CellValue?.Text;
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

    private static IReadOnlyList<ParsedUnit> ParseXls(string path)
    {
        var list = new List<ParsedUnit>();
        using var fs = File.OpenRead(path);
        var wb = new HSSFWorkbook(fs);
        for (var i = 0; i < wb.NumberOfSheets; i++)
        {
            var sheet = wb.GetSheetAt(i);
            var sb = new StringBuilder();
            for (var r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row is null) continue;
                for (var c = row.FirstCellNum; c < row.LastCellNum; c++)
                {
                    var v = GetNpoiCellString(row.GetCell(c));
                    if (!string.IsNullOrWhiteSpace(v))
                        sb.Append(v).Append(' ');
                }
                sb.AppendLine();
            }
            var text = sb.ToString().Trim();
            if (text.Length > 0)
                list.Add(new ParsedUnit("sheet: " + sheet.SheetName, text));
        }
        return list;
    }

    private static string? GetNpoiCellString(ICell? cell)
    {
        if (cell is null) return null;
        return cell.CellType switch
        {
            NPOI.SS.UserModel.CellType.String => cell.StringCellValue,
            NPOI.SS.UserModel.CellType.Numeric => cell.NumericCellValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NPOI.SS.UserModel.CellType.Boolean => cell.BooleanCellValue.ToString(),
            NPOI.SS.UserModel.CellType.Formula => cell.CellFormula,
            _ => null
        };
    }

    private static IReadOnlyList<ParsedUnit> ParseImage(string path)
    {
        var tessdata = FindTessdataPath();
        if (tessdata is null)
            return new List<ParsedUnit> { new(null, "이미지 파일: " + System.IO.Path.GetFileName(path) + " (OCR 언어 데이터 없음)") };

        try
        {
            var lang = DetectLanguage(tessdata);
            using var engine = new TesseractEngine(tessdata, lang, EngineMode.Default);
            engine.SetVariable("debug_file", "NUL");
            using var img = Pix.LoadFromFile(path);
            using var page = engine.Process(img);
            var text = page.GetText()?.Trim() ?? string.Empty;
            if (text.Length > 0)
                return new List<ParsedUnit> { new(null, text) };
            return new List<ParsedUnit> { new(null, "이미지 파일: " + System.IO.Path.GetFileName(path) + " (텍스트 없음)") };
        }
        catch
        {
            return new List<ParsedUnit> { new(null, "이미지 파일: " + System.IO.Path.GetFileName(path) + " (OCR 실패)") };
        }
    }

    private static string? FindTessdataPath()
    {
        var home = AppPaths.AppHomeDir;
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        {
            System.IO.Path.Combine(home, "tesseract", "tessdata"),
            System.IO.Path.Combine(home, "tessdata"),
            Environment.GetEnvironmentVariable("TESSDATA_PREFIX") ?? "",
            System.IO.Path.Combine(userHome, "qman", "tessdata"),
            "tessdata",
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "tessdata"),
            @"C:\Program Files\Tesseract-OCR\tessdata",
            @"C:\Program Files (x86)\Tesseract-OCR\tessdata"
        };

        foreach (var p in candidates)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            try
            {
                if (Directory.Exists(p) &&
                    (File.Exists(System.IO.Path.Combine(p, "eng.traineddata")) ||
                     File.Exists(System.IO.Path.Combine(p, "kor.traineddata"))))
                    return p;
            }
            catch { /* ignore */ }
        }

        return null;
    }

    private static string DetectLanguage(string tessdataPath)
    {
        try
        {
            var hasKor = File.Exists(System.IO.Path.Combine(tessdataPath, "kor.traineddata"));
            var hasEng = File.Exists(System.IO.Path.Combine(tessdataPath, "eng.traineddata"));
            if (hasKor && hasEng) return "kor+eng";
            if (hasKor) return "kor";
            if (hasEng) return "eng";
        }
        catch { /* ignore */ }
        return "eng";
    }
}
