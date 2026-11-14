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

app.MapFallback(static (HttpContext context) => Results.Problem(
    statusCode: StatusCodes.Status404NotFound,
    title: "Resource not found",
    detail: $"No endpoint is mapped to '{context.Request.Path}'."));

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
