namespace Domain.Persistence.Configuration;

public class LLMServiceOptions
{
    public int COMPUTE_BATCH_SIZE { get; set; } = 100;
    public List<string> LLM_SERVICE_URLS { get; set; } = [];
    public string LLM_SERVICE_URL { get; set; } = string.Empty;
}

public class DatabaseOptions
{
    public int PORT { get; set; }
    /// <summary>Qdrant serves REST on PORT (6333) and gRPC on a separate port (6334).</summary>
    public int GRPC_PORT { get; set; } = 6334;
    public int SAVE_BATCH_SIZE { get; set; }
    public int MAX_RETRY_COUNT { get; set; }
    public bool USE_HTTPS { get; set; } = false;
    public int MAX_CONNECTION_COUNT { get; set; } = 10;
    public string HOST { get; set; } = " ";
    public string DatabaseName { get; set; } = " ";
    public string QDRANT_API_KEY { get; set; } = " ";
    public string CollectionName { get; set; } = " ";
    public string ConnectionString { get; set; } = " ";    
    public string CollectionNameTwo { get; set; } = " ";
}