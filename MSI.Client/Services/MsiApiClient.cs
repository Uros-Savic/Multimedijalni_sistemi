using System.Net.Http.Json;
using System.Text.Json;

namespace MSI.Client.Services;

public record UploadResponse(string ImageId, string SessionId, int Width, int Height, string Format, long FileSizeBytes, string Message);
public record FilterStep(string Name, Dictionary<string, string> Parameters);
public record FilterRequest(string SessionId, List<FilterStep> Filters, string OutputFormat = "png", byte MsiCompression = 0, byte MsiColorspace = 1);
public record FilterResponse(string ImageId, string AppliedFilters, long ProcessingMs, string DownloadUrl, string PreviewBase64, int Width, int Height, long OutputSizeBytes);
public record ErrorResponse(string Error, string Details);

public sealed class MsiApiClient : IDisposable
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string BaseUrl { get; set; }

    public MsiApiClient(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task<UploadResponse> UploadAsync(string filePath, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(filePath);
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fs);
        string ext = Path.GetExtension(filePath).ToLowerInvariant().TrimStart('.');
        streamContent.Headers.ContentType = new(GetMimeType(ext));
        content.Add(streamContent, "file", Path.GetFileName(filePath));

        var resp = await _http.PostAsync($"{BaseUrl}/api/images/upload", content, ct);
        return await ParseResponse<UploadResponse>(resp);
    }

    public async Task<FilterResponse> ApplyFiltersAsync(FilterRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"{BaseUrl}/api/images/filter", request, JsonOpts, ct);
        return await ParseResponse<FilterResponse>(resp);
    }

    public async Task<byte[]> DownloadAsync(string sessionId, string resultId, string format, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"{BaseUrl}/api/images/download/{sessionId}/{resultId}/{format}", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"{BaseUrl}/api/images/health", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        try
        {
            await _http.DeleteAsync($"{BaseUrl}/api/images/session/{sessionId}", ct);
        }
        catch { }
    }

    public async Task RestoreCurrentAsync(string sessionId, byte[] pngBytes, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var byteContent = new ByteArrayContent(pngBytes);
        byteContent.Headers.ContentType = new("image/png");
        content.Add(byteContent, "file", "current.png");

        var resp = await _http.PostAsync($"{BaseUrl}/api/images/restore/{sessionId}", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            string body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Restore greska {(int)resp.StatusCode}: {body}");
        }
    }

    private static async Task<T> ParseResponse<T>(HttpResponseMessage resp)
    {
        string body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            ErrorResponse? err = null;
            try { err = JsonSerializer.Deserialize<ErrorResponse>(body, JsonOpts); } catch { }
            throw new HttpRequestException(
                $"Server greska {(int)resp.StatusCode}: {err?.Details ?? err?.Error ?? body}");
        }
        return JsonSerializer.Deserialize<T>(body, JsonOpts)
            ?? throw new InvalidOperationException("Server je vratio prazan odgovor.");
    }

    private static string GetMimeType(string ext) => ext switch
    {
        "png" => "image/png",
        "jpg" => "image/jpeg",
        "jpeg" => "image/jpeg",
        "bmp" => "image/bmp",
        "gif" => "image/gif",
        _ => "application/octet-stream"
    };

    public void Dispose() => _http.Dispose();
}