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
    public AppSettingsDao Settings { get; }
    public bool NeedsInitialSetup { get; }
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
        Db = new SqliteDb();
        Config = Db.Config;
        Settings = new AppSettingsDao(Db.Connection);
        NeedsInitialSetup = !AppSettingsDao.IsSetupComplete(Db.Connection);

        Categories = new CategoryDao(Db.Connection);
        Documents = new DocumentDao(Db.Connection);
        Chunks = new ChunkDao(Db.Connection);
        Embeddings = new EmbeddingDao(Db.Connection);
        Vec = new VecDao(Db);

        Llm = Config.LlmProvider switch
        {
            LlmProvider.Ollama => new OllamaClient(Config),
            LlmProvider.Claude => new ClaudeClient(Config),
            LlmProvider.GoogleAi => new GoogleAiClient(Config),
            LlmProvider.AlibabaCloud => new AlibabaCloudClient(Config),
            LlmProvider.DsPlayground => new OpenAiClient(Config),
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
