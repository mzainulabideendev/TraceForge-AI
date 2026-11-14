using Microsoft.EntityFrameworkCore;
using TraceForge.Application.Audit;
using TraceForge.Application.Common;
using TraceForge.Domain.Audit;
using TraceForge.Infrastructure.Persistence;

namespace TraceForge.Infrastructure.Audit;

internal sealed class AuditLogWriter : IAuditLogWriter
{
    private readonly TraceForgeDbContext _dbContext;

    public AuditLogWriter(TraceForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _dbContext.AuditLogs.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class AuditLogReader : IAuditLogReader
{
    private readonly TraceForgeDbContext _dbContext;

    public AuditLogReader(TraceForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<AuditLogRecord>> ReadAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedSize) = Paging.Normalize(page, pageSize);

        var query = _dbContext.AuditLogs
            .AsNoTracking()
            .OrderByDescending(a => a.OccurredAtUtc)
            .ThenByDescending(a => a.Id);

        var total = await query.CountAsync(cancellationToken);

        var entities = await query
            .Skip(Paging.Skip(normalizedPage, normalizedSize))
            .Take(normalizedSize)
            .ToListAsync(cancellationToken);

        var items = entities
            .Select(a => new AuditLogRecord(
                a.Id,
                a.ActorUserId,
                a.ActorIdentifier,
                a.Action,
                a.TargetType,
                a.TargetId,
                a.Outcome.ToString(),
                a.IpAddress,
                a.UserAgent,
                a.Details,
                a.OccurredAtUtc))
            .ToList();

        return new PagedResult<AuditLogRecord>(items, normalizedPage, normalizedSize, total);
    }
}
