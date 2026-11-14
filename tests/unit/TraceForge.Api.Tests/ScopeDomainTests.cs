using TraceForge.Domain.Scopes;

namespace TraceForge.Api.Tests;

public sealed class ScopeDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static AuthorizedScope CreateScope(
        string restrictions = "",
        DateTimeOffset? expiresAt = null) =>
        AuthorizedScope.Create(
            Guid.NewGuid(),
            ScopeType.IPv4,
            "8.8.8.8",
            restrictions,
            Now,
            expiresAt ?? Now.AddDays(30),
            Guid.NewGuid(),
            Now);

    [Fact]
    public void NewScope_IsPendingAndNotUsable()
    {
        var scope = CreateScope();

        Assert.Equal(ScopeApprovalStatus.Pending, scope.Status);
        Assert.False(scope.IsUsable(Now));
        Assert.Null(scope.ApprovedAtUtc);
    }

    [Fact]
    public void Approval_RequiresEvidenceAndRecordsApprover()
    {
        var scope = CreateScope();
        var approver = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => scope.Approve(approver, "  ", Now));

        scope.Approve(approver, "CHANGE-1234", Now);

        Assert.Equal(ScopeApprovalStatus.Approved, scope.Status);
        Assert.Equal(approver, scope.ApprovedByUserId);
        Assert.Equal(Now, scope.ApprovedAtUtc);
        Assert.True(scope.IsUsable(Now));
    }

    [Fact]
    public void RevokedScope_CannotBeApprovedAgain()
    {
        var scope = CreateScope();
        var user = Guid.NewGuid();

        scope.Revoke(user, Now);

        Assert.Equal(ScopeApprovalStatus.Revoked, scope.Status);
        Assert.Throws<InvalidOperationException>(() => scope.Approve(user, "CHANGE-1", Now));
    }

    [Fact]
    public void ExpiredScope_IsNotUsableAndCannotBeApproved()
    {
        var scope = CreateScope(expiresAt: Now.AddMinutes(5));
        var user = Guid.NewGuid();

        Assert.False(scope.IsUsable(Now.AddMinutes(6)));
        Assert.Throws<InvalidOperationException>(() => scope.Approve(user, "CHANGE-1", Now.AddMinutes(6)));

        scope.Expire(Now.AddMinutes(6));
        Assert.Equal(ScopeApprovalStatus.Expired, scope.Status);

        // Expiry is idempotent: repeating it after the deadline changes nothing.
        scope.Expire(Now.AddMinutes(7));
        Assert.Equal(ScopeApprovalStatus.Expired, scope.Status);
    }

    [Fact]
    public void Expire_BeforeDeadline_IsRejected()
    {
        var scope = CreateScope();
        Assert.Throws<InvalidOperationException>(() => scope.Expire(Now));
    }

    [Fact]
    public void ChangingRestrictions_ResetsApprovalToPending()
    {
        var scope = CreateScope();
        var user = Guid.NewGuid();
        scope.Approve(user, "CHANGE-9", Now);

        scope.UpdateRestrictions([ScopeRestrictions.PrivateNetwork], Now);

        Assert.Equal(ScopeApprovalStatus.Pending, scope.Status);
        Assert.Null(scope.ApprovalReference);
        Assert.Null(scope.ApprovedByUserId);
        Assert.Null(scope.ApprovedAtUtc);
        Assert.True(scope.HasRestriction(ScopeRestrictions.PrivateNetwork));
    }

    [Fact]
    public void ExpirationMustBeAfterValidFromAndInTheFuture()
    {
        Assert.Throws<ArgumentException>(() => AuthorizedScope.Create(
            Guid.NewGuid(),
            ScopeType.IPv4,
            "8.8.8.8",
            string.Empty,
            Now,
            Now,
            Guid.NewGuid(),
            Now));

        Assert.Throws<ArgumentException>(() => AuthorizedScope.Create(
            Guid.NewGuid(),
            ScopeType.IPv4,
            "8.8.8.8",
            string.Empty,
            Now.AddHours(-2),
            Now.AddHours(-1),
            Guid.NewGuid(),
            Now));
    }
}
