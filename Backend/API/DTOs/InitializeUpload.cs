using System.Diagnostics.CodeAnalysis;

namespace API.DTOs;

[ExcludeFromCodeCoverage]
public class InitializeUploadRequest
{
    public string Filename { get; set; } = string.Empty;
}

[ExcludeFromCodeCoverage]
public class InitializeUploadResponse : BaseResponse
{
}
