using TraceForge.Application.Common;

namespace TraceForge.Api.Tests;

/// <summary>Deterministic clock for service-level tests.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;

    public void Set(DateTimeOffset value) => _utcNow = value;
}

/// <summary>A current user fixed for the duration of a service-level test.</summary>
internal sealed class StubCurrentUser : ICurrentUser
{
    public StubCurrentUser(Guid? userId, string? email = null)
    {
        UserId = userId;
        Email = email;
    }

    public bool IsAuthenticated => UserId is not null;

    public Guid? UserId { get; }

    public string? Email { get; }
}

/// <summary>Resolver whose answers the test controls, so no real DNS is queried.</summary>
internal sealed class StubDnsResolver : TraceForge.Application.Scopes.IDnsResolver
{
    private readonly Queue<IReadOnlyList<System.Net.IPAddress>> _responses = new();

    public void Enqueue(params System.Net.IPAddress[] addresses) => _responses.Enqueue(addresses);

    public Task<IReadOnlyList<System.Net.IPAddress>> ResolveAsync(
        string hostname,
        CancellationToken cancellationToken)
    {
        var response = _responses.Count > 0 ? _responses.Dequeue() : [];
        return Task.FromResult(response);
    }
}
