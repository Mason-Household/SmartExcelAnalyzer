using System.Diagnostics.CodeAnalysis;

namespace API.DTOs;

[ExcludeFromCodeCoverage]
public class BaseResponse
{
    public string DocumentId { get; set; } = string.Empty;
    public string? Filename { get; set; }
}