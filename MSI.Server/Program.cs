using Serilog;
using Serilog.Events;
using MSI.Server.Middleware;
using MSI.Server.Services;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/msi-server-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("MSI Server se pokrece...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "MSI Image Processing API", Version = "v1" });
    });

    builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
    {
        o.MultipartBodyLengthLimit = 20 * 1024 * 1024;
    });

    builder.Services.AddSingleton<SessionService>();
    builder.Services.AddScoped<ImageProcessingService>();
    builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

    var app = builder.Build();
    app.UseMiddleware<RequestLoggingMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MSI API v1"));
    }

    app.UseCors();
    app.UseAuthorization();
    app.MapControllers();
    app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
    {
        var err = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        Log.Error(err?.Error, "Neobradjena greska u requestu {Path}", ctx.Request.Path);
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            Error = "Interna greska servera.",
            Details = app.Environment.IsDevelopment() ? err?.Error?.Message : null
        });
    }));

    Log.Information("MSI Server pokrenut. Slusam na {Url}", "http://localhost:5000");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "MSI Server se srusio pri pokretanju!");
}
finally
{
    Log.CloseAndFlush();
}
