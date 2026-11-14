using Microsoft.Extensions.Options;
using Serilog;
using TraceForge.Api;
using TraceForge.Api.Configuration;
using TraceForge.Api.Endpoints;
using TraceForge.Api.Middleware;
using TraceForge.Api.Security;
using TraceForge.Infrastructure;
using TraceForge.Infrastructure.Configuration;
using TraceForge.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", TraceForgeIdentity.Name)
    .Enrich.WithProperty("Version", TraceForgeIdentity.InformationalVersion));

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddTraceForgeCore(builder.Configuration);
builder.Services.AddTraceForgeInfrastructure(builder.Configuration);
builder.Services.AddTraceForgeAuthentication();

var app = builder.Build();

app.UseExceptionHandler();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

// The Angular console is optional. When a build is present under wwwroot the API serves it from the
// same origin as /api, which keeps the session and antiforgery cookies first-party (SameSite=Strict).
var spaIndexPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
var spaEnabled = File.Exists(spaIndexPath);

if (spaEnabled)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    Log.Information("Serving the built web console from {WebRoot}.", Path.GetDirectoryName(spaIndexPath));
}

app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    // The OpenAPI document is a development aid; it is not exposed by default in other environments.
    app.MapOpenApi();
}

app.MapTraceForgeSystem();

var api = app.MapGroup("/api").AddEndpointFilter<AntiforgeryEndpointFilter>();

api.MapAuthEndpoints();
api.MapAdminEndpoints();
api.MapWorkspaceEndpoints();
api.MapScopeEndpoints();

// Unknown application routes fall back to the SPA document so client-side routing works on a hard
// refresh. API and health paths, and requests for a file with an extension, still return JSON 404.
app.MapFallback((HttpContext context) =>
{
    var isRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
    var isSpaRoute =
        isRead &&
        !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
        !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) &&
        !Path.HasExtension(context.Request.Path.Value);

    return spaEnabled && isSpaRoute
        ? Results.File(spaIndexPath, "text/html; charset=utf-8")
        : Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Resource not found",
            detail: $"No endpoint is mapped to '{context.Request.Path}'.");
});

var databaseOptions = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

if (databaseOptions.ApplyMigrationsOnStartup)
{
    var result = await DatabaseInitializer.InitializeAsync(app.Services);
    Log.Information(
        "Schema migration complete: applied={Applied}, rolesSeededCount={RolesSeededCount}",
        result.MigrationsApplied,
        result.RolesSeeded.Count);
}
else
{
    Log.Warning(
        "Automatic migration is disabled (TraceForge:Database:ApplyMigrationsOnStartup=false). " +
        "The schema must be migrated out of band or readiness will report the database as unusable.");
}

app.Run();
