using QMan.Core;
using QMan.Data;
using QMan.Ingestion;
using QMan.Llm;
using QMan.Rag;

namespace QMan.App;

public sealed class AppContextRoot : IDisposable
{
    private static AppContextRoot? _instance;

    public static AppContextRoot Instance => _instance ??= new AppContextRoot();

    public AppConfig Config { get; }
    public SqliteDb Db { get; }
    public CategoryDao Categories { get; }
    public DocumentDao Documents { get; }
    public ChunkDao Chunks { get; }
    public EmbeddingDao Embeddings { get; }
    public VecDao Vec { get; }
    public ILlmClient Llm { get; }
    public SearchService Search { get; }
    public RagService Rag { get; }
    public IngestionService Ingestion { get; }

    private AppContextRoot()
    {
        Config = AppConfig.Load();
        Db = new SqliteDb(Config);

        Categories = new CategoryDao(Db.Connection);
        Documents = new DocumentDao(Db.Connection);
        Chunks = new ChunkDao(Db.Connection);
        Embeddings = new EmbeddingDao(Db.Connection);
        Vec = new VecDao(Db);

        Llm = Config.LlmProvider switch
        {
            LlmProvider.Ollama => new OllamaClient(Config),
            _ => new OpenAiClient(Config)
        };

        Search = new SearchService(Db, Vec);
        Rag = new RagService(Config, Db, Llm, Search, Embeddings, Vec);
        Ingestion = new IngestionService(Documents, Chunks);
    }

    public static void Shutdown()
    {
        _instance?.Dispose();
        _instance = null;
    }

    public void Dispose()
    {
        Db.Dispose();
    }
}
