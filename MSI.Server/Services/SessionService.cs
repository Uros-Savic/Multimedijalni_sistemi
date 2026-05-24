using System.Collections.Concurrent;
using MSI.Server.Models;

namespace MSI.Server.Services;

public sealed class SessionService : IDisposable
{
    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();
    private readonly string _storageDir;
    private readonly ILogger<SessionService> _logger;

    public SessionService(ILogger<SessionService> logger, IConfiguration config)
    {
        _logger     = logger;
        _storageDir = config["Storage:Dir"] ?? Path.Combine(Path.GetTempPath(), "msi_server");
        Directory.CreateDirectory(_storageDir);
        _logger.LogInformation("SessionService pokrenut. Storage: {Dir}", _storageDir);
    }

    public SessionInfo CreateSession(string originalFilename)
    {
        string sessionId = Guid.NewGuid().ToString("N");
        string dir       = Path.Combine(_storageDir, sessionId);
        Directory.CreateDirectory(dir);

        var session = new SessionInfo
        {
            SessionId        = sessionId,
            ImageId          = Guid.NewGuid().ToString("N"),
            Created          = DateTime.UtcNow,
            LastAccessed     = DateTime.UtcNow,
            OriginalFilename = Path.GetFileName(originalFilename),
            StoragePath      = dir
        };
        _sessions[sessionId] = session;
        _logger.LogInformation("Nova sesija: {Id} za '{File}'", sessionId, originalFilename);
        return session;
    }

    public SessionInfo? GetSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var s))
        {
            s.LastAccessed = DateTime.UtcNow;
            return s;
        }
        return null;
    }

    public string GetImagePath(string sessionId, string suffix = "current")
{
    string dir = Path.Combine(_storageDir, sessionId);
    if (!Directory.Exists(dir))
        Directory.CreateDirectory(dir);
    return Path.Combine(dir, $"image_{suffix}.png");
}

    public void DeleteSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var s))
        {
            try
            {
                if (Directory.Exists(s.StoragePath))
                    Directory.Delete(s.StoragePath, recursive: true);
                _logger.LogInformation("Sesija {Id} obrisana.", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nije moguce obrisati storage za sesiju {Id}", sessionId);
            }
        }
    }

    public void Dispose() { }
}