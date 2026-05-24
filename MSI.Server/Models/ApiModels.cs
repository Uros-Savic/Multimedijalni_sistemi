namespace MSI.Server.Models;

public sealed class UploadResponse
{
    public string ImageId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public string Format { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class FilterStep
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public sealed class FilterRequest
{
    public string SessionId { get; set; } = string.Empty;
    public List<FilterStep> Filters { get; set; } = new();
    public string OutputFormat { get; set; } = "png";
    public byte MsiCompression { get; set; } = 0;
    public byte MsiColorspace { get; set; } = 1;
}

public sealed class FilterResponse
{
    public string ImageId { get; init; } = string.Empty;
    public string AppliedFilters { get; init; } = string.Empty;
    public long ProcessingMs { get; init; }
    public string DownloadUrl { get; init; } = string.Empty;
    public string PreviewBase64 { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public long OutputSizeBytes { get; init; }
}

public sealed class SessionInfo
{
    public string SessionId { get; set; } = string.Empty;
    public string ImageId { get; set; } = string.Empty;
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
    public string OriginalFilename { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
}

public sealed class ErrorResponse
{
    public string Error { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
}

public sealed class RestoreCurrentResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}