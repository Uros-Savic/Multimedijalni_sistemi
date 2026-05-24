using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using MSI.Core.Filters;
using MSI.Core.MsiFormat;
using MSI.Server.Models;

namespace MSI.Server.Services;

public sealed class ImageProcessingService
{
    private readonly ILogger<ImageProcessingService> _logger;
    private readonly SessionService _sessions;
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".msi" };
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/png", "image/jpeg", "image/bmp", "image/gif", "application/octet-stream" };
    private const long MaxUploadBytes = 20 * 1024 * 1024;

    public ImageProcessingService(ILogger<ImageProcessingService> logger, SessionService sessions)
    {
        _logger = logger;
        _sessions = sessions;
    }

    public async Task RestoreCurrentAsync(string sessionId, byte[] pngBytes)
    {
        var session = _sessions.GetSession(sessionId)
            ?? throw new KeyNotFoundException($"Sesija '{sessionId}' nije pronadjena.");

        string currentPath = _sessions.GetImagePath(sessionId, "current");

        using var ms = new MemoryStream(pngBytes);
        using var bmp = new Bitmap(ms);
        bmp.Save(currentPath, ImageFormat.Png);

        _logger.LogInformation("RestoreCurrent: sesija={S} dim={W}x{H}", sessionId, bmp.Width, bmp.Height);
        await Task.CompletedTask;
    }


    public async Task<UploadResponse> UploadImageAsync(IFormFile file)
    {
        ValidateUpload(file);
        var session = _sessions.CreateSession(file.FileName);
        string savedPath = _sessions.GetImagePath(session.SessionId, "original");
        try
        {
            await using var stream = file.OpenReadStream();
            Bitmap bmp;
            string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext == ".msi")
            {
                var decoder = new MsiDecoder();
                using var memStream = new MemoryStream();
                await stream.CopyToAsync(memStream);
                byte[] bytes = memStream.ToArray();
                (bmp, _) = decoder.Decode(bytes);
            }
            else
            {
                bmp = new Bitmap(stream);
            }
            using (bmp)
            {
                bmp.Save(savedPath, ImageFormat.Png);
                _logger.LogInformation("Upload OK: session={S} size={W}x{H}", session.SessionId, bmp.Width, bmp.Height);

                return new UploadResponse
                {
                    ImageId = session.ImageId,
                    SessionId = session.SessionId,
                    Width = bmp.Width,
                    Height = bmp.Height,
                    Format = ext.TrimStart('.').ToUpper(),
                    FileSizeBytes = file.Length,
                    Message = "Slika uspesno uploadovana."
                };
            }
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            _logger.LogError(ex, "Greska pri uploadu slike za sesiju {S}", session.SessionId);
            _sessions.DeleteSession(session.SessionId);
            throw new InvalidOperationException($"Greska pri obradi slike: {ex.Message}", ex);
        }
    }

    public async Task<FilterResponse> ApplyFiltersAsync(FilterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            throw new ArgumentException("SessionId je obavezan.");
        if (request.Filters == null || request.Filters.Count == 0)
            throw new ArgumentException("Mora biti navedeno barem jedan filter.");
        if (request.Filters.Count > 20)
            throw new ArgumentException("Maksimalan broj filtera u jednom pozivu je 20.");

        var session = _sessions.GetSession(request.SessionId)
            ?? throw new KeyNotFoundException($"Sesija '{request.SessionId}' nije pronadjena.");

        string currentPath = _sessions.GetImagePath(request.SessionId, "current");
        string originalPath = _sessions.GetImagePath(request.SessionId, "original");
        string srcPath = File.Exists(currentPath) ? currentPath : originalPath;

        if (!File.Exists(srcPath))
            throw new FileNotFoundException("Slika nije pronadjena na serveru.");

        var sw = Stopwatch.StartNew();
        Bitmap? current = null;
        string filterLog = string.Join(" -> ", request.Filters.Select(f => f.Name));

        try
        {
            current = new Bitmap(srcPath);

            foreach (var step in request.Filters)
            {
                _logger.LogInformation("Primena filtera '{F}' na sesiju {S}", step.Name, request.SessionId);
                if (!FilterFactory.Exists(step.Name))
                    throw new ArgumentException($"Nepoznat filter: '{step.Name}'");

                var filter = FilterFactory.Get(step.Name);
                var parameters = new FilterParameters(step.Parameters);
                var sw2 = Stopwatch.StartNew();
                var next = filter.Apply(current, parameters);
                sw2.Stop();
                _logger.LogInformation("Filter '{F}' zavrsen za {Ms}ms", step.Name, sw2.ElapsedMilliseconds);

                current.Dispose();
                current = next;
            }

            sw.Stop();
            current.Save(currentPath, ImageFormat.Png);
            string resultId = Guid.NewGuid().ToString("N")[..8];
            string previewB64 = GeneratePreviewBase64(current);
            byte[] outputBytes = SerializeOutput(current, request.OutputFormat, request.MsiCompression, request.MsiColorspace);
            string dlPath = Path.Combine(session.StoragePath, $"download_{resultId}.{request.OutputFormat}");
            await File.WriteAllBytesAsync(dlPath, outputBytes);

            _logger.LogInformation("Filteri {F} zavrseni za {Ms}ms na sesiji {S}", filterLog, sw.ElapsedMilliseconds, request.SessionId);

            return new FilterResponse
            {
                ImageId = $"{session.ImageId}_{resultId}",
                AppliedFilters = filterLog,
                ProcessingMs = sw.ElapsedMilliseconds,
                DownloadUrl = $"/api/images/download/{session.SessionId}/{resultId}/{request.OutputFormat}",
                PreviewBase64 = previewB64,
                Width = current.Width,
                Height = current.Height,
                OutputSizeBytes = outputBytes.Length
            };
        }
        finally
        {
            current?.Dispose();
        }
    }

    public byte[] GetDownloadBytes(string sessionId, string resultId, string format)
    {
        var session = _sessions.GetSession(sessionId)
            ?? throw new KeyNotFoundException($"Sesija '{sessionId}' nije pronadjena.");
        string path = Path.Combine(session.StoragePath, $"download_{resultId}.{format}");
        if (!File.Exists(path))
            throw new FileNotFoundException("Trazeni fajl nije pronadjen.");

        var info = new FileInfo(path);
        _logger.LogInformation(
            "Download: sesija={S} resultId={R} format={F} velicina={Kb:F1}KB",
            sessionId, resultId, format, info.Length / 1024.0);

        return File.ReadAllBytes(path);
    }

    private static void ValidateUpload(IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("Fajl je prazan.");
        if (file.Length > MaxUploadBytes)
            throw new ArgumentException($"Fajl je prevelik. Maksimum je {MaxUploadBytes / 1024 / 1024} MB.");

        string ext = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new ArgumentException($"Ekstenzija '{ext}' nije dozvoljena. Dozvoljene: {string.Join(", ", AllowedExtensions)}");

        string safeName = Path.GetFileName(file.FileName ?? "");
        if (safeName != file.FileName && !string.IsNullOrEmpty(file.FileName))
            throw new ArgumentException("Ime fajla sadrzi nedozvoljene karaktere.");
    }

    private static string GeneratePreviewBase64(Bitmap bmp)
    {
        int thumbW = Math.Min(bmp.Width, 200);
        int thumbH = (int)(bmp.Height * (double)thumbW / bmp.Width);
        thumbH = Math.Min(thumbH, 150);

        using var thumb = new Bitmap(bmp, thumbW, thumbH);
        using var ms = new MemoryStream();
        thumb.Save(ms, ImageFormat.Png);
        return Convert.ToBase64String(ms.ToArray());
    }

    private byte[] SerializeOutput(Bitmap bmp, string format, byte msiComp, byte colorspace = 1)
    {
        using var ms = new MemoryStream();
        string fmt = format.ToLowerInvariant();

        switch (fmt)
        {
            case "png": bmp.Save(ms, ImageFormat.Png); break;
            case "jpeg":
            case "jpg": bmp.Save(ms, ImageFormat.Jpeg); break;
            case "bmp": bmp.Save(ms, ImageFormat.Bmp); break;
            case "gif": bmp.Save(ms, ImageFormat.Gif); break;
            case "msi":
                new MsiEncoder().Encode(bmp, ms,
                    colorspace: colorspace,
                    compression: msiComp);
                break;
            default:
                throw new ArgumentException($"Nepoznat izlazni format: '{format}'");
        }

        byte[] result = ms.ToArray();
        _logger.LogInformation(
            "SerializeOutput: format={F} colorspace={CS} compression={C} velicina={Kb:F1}KB dim={W}x{H}",
            fmt.ToUpper(),
            fmt == "msi" ? colorspace.ToString() : "N/A",
            fmt == "msi" ? msiComp.ToString() : "N/A",
            result.Length / 1024.0,
            bmp.Width, bmp.Height);

        return result;
    }
}