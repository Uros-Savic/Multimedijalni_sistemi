using System.Diagnostics;

namespace MSI.Server.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        string ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string ua = context.Request.Headers.UserAgent.FirstOrDefault() ?? "";

        try
        {
            await _next(context);
            sw.Stop();
            _logger.LogInformation(
                "[{Method}] {Path} | Status: {Status} | IP: {IP} | {Ms}ms | UA: {UA}",
                context.Request.Method,
                context.Request.Path + context.Request.QueryString,
                context.Response.StatusCode,
                ip, sw.ElapsedMilliseconds, ua);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "[{Method}] {Path} | GREsKA | IP: {IP} | {Ms}ms",
                context.Request.Method,
                context.Request.Path,
                ip, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
