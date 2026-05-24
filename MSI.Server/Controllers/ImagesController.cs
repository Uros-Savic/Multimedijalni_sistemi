using Microsoft.AspNetCore.Mvc;
using MSI.Core.Filters;
using MSI.Server.Models;
using MSI.Server.Services;

namespace MSI.Server.Controllers;

[ApiController]
[Route("api/images")]
[Produces("application/json")]
public sealed class ImagesController : ControllerBase
{
    private readonly ImageProcessingService _processor;
    private readonly ILogger<ImagesController> _logger;
    public ImagesController(ImageProcessingService processor, ILogger<ImagesController> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    [ProducesResponseType(typeof(UploadResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        try
        {
            var result = await _processor.UploadImageAsync(file);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Upload odbijen: {Msg}", ex.Message);
            return BadRequest(new ErrorResponse { Error = "Validaciona greska", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Neocekivana greska pri uploadu");
            return StatusCode(500, new ErrorResponse { Error = "Interna greska servera", Details = ex.Message });
        }
    }

    [HttpPost("filter")]
    [ProducesResponseType(typeof(FilterResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> Filter([FromBody] FilterRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new ErrorResponse { Error = "Nevazeci zahtev", Details = ModelState.ToString() ?? "" });

        try
        {
            var result = await _processor.ApplyFiltersAsync(request);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Filter odbijen: {Msg}", ex.Message);
            return BadRequest(new ErrorResponse { Error = "Validaciona greska", Details = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Error = "Nije pronadjeno", Details = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Error = "Fajl nije pronadjen", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Neocekivana greska pri primeni filtera");
            return StatusCode(500, new ErrorResponse { Error = "Interna greska servera", Details = ex.Message });
        }
    }

    [HttpGet("download/{sessionId}/{resultId}/{format}")]
    [ProducesResponseType(typeof(FileContentResult), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    public IActionResult Download(string sessionId, string resultId, string format)
    {
        if (!IsAlphanumeric(sessionId) || !IsAlphanumeric(resultId))
            return BadRequest(new ErrorResponse { Error = "Nevazeci ID parametri." });

        var allowedFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "png", "jpeg", "jpg", "bmp", "gif", "msi" };
        if (!allowedFormats.Contains(format))
            return BadRequest(new ErrorResponse { Error = $"Nevazeci format '{format}'." });

        try
        {
            byte[] bytes = _processor.GetDownloadBytes(sessionId, resultId, format);
            string mimeType = GetMimeType(format);
            string filename = $"output_{resultId}.{format}";
            return File(bytes, mimeType, filename);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Error = "Sesija nije pronadjena", Details = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Error = "Fajl nije pronadjen", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Greska pri preuzimanju");
            return StatusCode(500, new ErrorResponse { Error = "Interna greska servera", Details = ex.Message });
        }
    }

    [HttpPost("restore/{sessionId}")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    [ProducesResponseType(typeof(RestoreCurrentResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public async Task<IActionResult> RestoreCurrent(string sessionId, IFormFile file)
    {
        if (!IsAlphanumeric(sessionId))
            return BadRequest(new ErrorResponse { Error = "Nevazeci sessionId." });
        if (file == null || file.Length == 0)
            return BadRequest(new ErrorResponse { Error = "Fajl je prazan." });

        try
        {
            using var memStream = new MemoryStream();
            await file.OpenReadStream().CopyToAsync(memStream);
            await _processor.RestoreCurrentAsync(sessionId, memStream.ToArray());
            return Ok(new RestoreCurrentResponse { Success = true, Message = "Current stanje postavljeno." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Error = "Sesija nije pronadjena.", Details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Greska pri restore sesije {S}", sessionId);
            return StatusCode(500, new ErrorResponse { Error = "Interna greska servera.", Details = ex.Message });
        }
    }

    [HttpDelete("session/{sessionId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    public IActionResult DeleteSession(string sessionId)
    {
        if (!IsAlphanumeric(sessionId))
            return BadRequest(new ErrorResponse { Error = "Nevazeci sessionId." });

        var sessions = HttpContext.RequestServices.GetRequiredService<SessionService>();
        sessions.DeleteSession(sessionId);
        _logger.LogInformation("Eksplicitno brisanje sesije {Id}", sessionId);
        return Ok(new { Deleted = sessionId });
    }

    private static bool IsAlphanumeric(string s)
        => !string.IsNullOrEmpty(s) && s.All(c => char.IsLetterOrDigit(c) || c == '-');

    private static string GetMimeType(string format) => format.ToLowerInvariant() switch
    {
        "png" => "image/png",
        "jpeg" => "image/jpeg",
        "jpg" => "image/jpeg",
        "bmp" => "image/bmp",
        "gif" => "image/gif",
        "msi" => "application/octet-stream",
        _ => "application/octet-stream"
    };
}