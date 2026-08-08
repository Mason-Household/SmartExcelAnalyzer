using System.Collections.Concurrent;

namespace Domain.Persistence.DTOs;

public class QueryAnswer
{
    public string Question { get; set; } = "";
    public string DocumentId { get; set; } = "";
    public required string Answer { get; set; }
    public ConcurrentBag<ConcurrentDictionary<string, object>> RelevantRows { get; set; } = [];
    /// <summary>
    /// Spreadsheet column order. Row payloads are unordered maps, so clients need
    /// this to line values up under the right headers.
    /// </summary>
    public List<string> Columns { get; set; } = [];
}