using QMan.Data;

namespace QMan.Ingestion;

public sealed class IngestionService
{
    public sealed record IngestResult(long DocumentId, int ChunkCount, IReadOnlyList<long> ChunkIds);

    private readonly DocumentDao _documentDao;
    private readonly ChunkDao _chunkDao;
    private readonly DocumentParserService _parser;
    private readonly Chunker _chunker;

    public IngestionService(DocumentDao documentDao, ChunkDao chunkDao)
    {
        _documentDao = documentDao;
        _chunkDao = chunkDao;
        _parser = new DocumentParserService();
        _chunker = new Chunker(1200, 150);
    }

    public IngestResult Ingest(long categoryId, string filePath)
    {
        long? sizeBytes = null;
        try { sizeBytes = new FileInfo(filePath).Length; } catch { /* ignore */ }

        var name = Path.GetFileName(filePath);
        var doc = _documentDao.Create(categoryId, name, "", sizeBytes);

        var chunkIndex = 0;
        var chunkIds = new List<long>();
        foreach (var unit in _parser.Parse(filePath))
        {
            foreach (var part in _chunker.Chunk(unit.Text))
            {
                var ch = _chunkDao.Insert(doc.Id, chunkIndex++, unit.SourceLabel, part);
                chunkIds.Add(ch.Id);
            }
        }

        return new IngestResult(doc.Id, chunkIndex, chunkIds);
    }

    public void DeleteDocument(long documentId) => _documentDao.Delete(documentId);
}
