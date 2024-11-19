using System.Diagnostics.CodeAnalysis;

namespace API.DTOs;

[ExcludeFromCodeCoverage]
public class FinalizeUploadRequest
{
    public string UploadId { get; set; } = string.Empty;
}

[ExcludeFromCodeCoverage]
public class FinalizeUploadResponse : BaseResponse
{
}
