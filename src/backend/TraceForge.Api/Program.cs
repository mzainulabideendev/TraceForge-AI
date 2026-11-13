using Serilog;
using TraceForge.Api;
using TraceForge.Api.Configuration;
using TraceForge.Api.Endpoints;
using TraceForge.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", TraceForgeIdentity.Name)
    .Enrich.WithProperty("Version", TraceForgeIdentity.InformationalVersion));

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddTraceForgeCore(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseSerilogRequestLogging();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    // The OpenAPI document is a development aid; it is not exposed by default in other environments.
    app.MapOpenApi();
}

app.MapTraceForgeSystem();

app.MapFallback(static (HttpContext context) => Results.Problem(
    statusCode: StatusCodes.Status404NotFound,
    title: "Resource not found",
    detail: $"No endpoint is mapped to '{context.Request.Path}'."));

app.Run();
