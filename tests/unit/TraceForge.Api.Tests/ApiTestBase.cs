namespace TraceForge.Api.Tests;

/// <summary>
/// Gives every test its own API instance and database file. Sharing one database per class made
/// assertions depend on execution order: only the very first registration of a database becomes
/// Administrator, so any later "register and expect Administrator" test would see Analyst.
/// </summary>
public abstract class ApiTestBase : IDisposable
{
    protected ApiTestBase()
    {
        Factory = TraceForgeApiFactory.WithConfiguration();
    }

    protected TraceForgeApiFactory Factory { get; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Factory.Dispose();
        }
    }
}
