using Microsoft.EntityFrameworkCore;
using TraceForge.Domain.Scopes;
using TraceForge.Domain.Workspaces;

namespace TraceForge.Infrastructure.Persistence;

/// <summary>
/// Phase 2 model configuration: workspace tenancy and authorized scopes.
/// Kept in a partial file so the Phase 1 model stays easy to review.
/// </summary>
public sealed partial class TraceForgeDbContext
{
    private static void ConfigureWorkspaces(ModelBuilder builder)
    {
        builder.Entity<Workspace>(workspace =>
        {
            workspace.ToTable("Workspaces");
            workspace.HasKey(w => w.Id);
            workspace.Property(w => w.Name).IsRequired().HasMaxLength(Workspace.NameMaxLength);
            workspace.Property(w => w.Description).HasMaxLength(Workspace.DescriptionMaxLength);
            workspace.Property(w => w.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            workspace.Property(w => w.CreatedAtUtc).IsRequired();
            workspace.Property(w => w.UpdatedAtUtc).IsRequired();
            workspace.Ignore(w => w.IsArchived);

            workspace.HasIndex(w => w.Status);
            workspace.HasIndex(w => w.CreatedAtUtc);

            workspace.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(w => w.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WorkspaceMembership>(membership =>
        {
            membership.ToTable("WorkspaceMemberships");
            membership.HasKey(m => m.Id);
            membership.Property(m => m.Role)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            membership.Property(m => m.CreatedAtUtc).IsRequired();
            membership.Property(m => m.UpdatedAtUtc).IsRequired();

            membership.HasIndex(m => new { m.WorkspaceId, m.UserId }).IsUnique();
            membership.HasIndex(m => m.UserId);

            membership.HasOne<Workspace>()
                .WithMany()
                .HasForeignKey(m => m.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            membership.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureScopes(ModelBuilder builder)
    {
        builder.Entity<AuthorizedScope>(scope =>
        {
            scope.ToTable("AuthorizedScopes");
            scope.HasKey(s => s.Id);
            scope.Property(s => s.Type)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            scope.Property(s => s.NormalizedTarget).IsRequired().HasMaxLength(ScopeTargetNormalizer.MaxTargetLength);
            scope.Property(s => s.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            scope.Property(s => s.ApprovalReference).HasMaxLength(AuthorizedScope.ApprovalReferenceMaxLength);
            scope.Property(s => s.Restrictions).IsRequired().HasMaxLength(ScopeRestrictions.MaxLength);
            scope.Property(s => s.ValidFromUtc).IsRequired();
            scope.Property(s => s.ExpiresAtUtc).IsRequired();
            scope.Property(s => s.CreatedAtUtc).IsRequired();
            scope.Property(s => s.UpdatedAtUtc).IsRequired();

            scope.HasIndex(s => new { s.WorkspaceId, s.Status });
            scope.HasIndex(s => new { s.WorkspaceId, s.Type, s.NormalizedTarget }).IsUnique();
            scope.HasIndex(s => s.ExpiresAtUtc);

            scope.HasOne<Workspace>()
                .WithMany()
                .HasForeignKey(s => s.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            scope.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(s => s.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            scope.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(s => s.ApprovedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            scope.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(s => s.RevokedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ScopeApproval>(approval =>
        {
            approval.ToTable("ScopeApprovals");
            approval.HasKey(a => a.Id);
            approval.Property(a => a.Reference).IsRequired().HasMaxLength(ScopeApproval.ReferenceMaxLength);
            approval.Property(a => a.ApprovedAtUtc).IsRequired();

            approval.HasIndex(a => a.ScopeId);
            approval.HasIndex(a => a.WorkspaceId);

            approval.HasOne<AuthorizedScope>()
                .WithMany()
                .HasForeignKey(a => a.ScopeId)
                .OnDelete(DeleteBehavior.Cascade);

            approval.HasOne<Workspace>()
                .WithMany()
                .HasForeignKey(a => a.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            approval.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(a => a.ApprovedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ScopeAuditEvent>(auditEvent =>
        {
            auditEvent.ToTable("ScopeAuditEvents");
            auditEvent.HasKey(e => e.Id);
            auditEvent.Property(e => e.Action).IsRequired().HasMaxLength(ScopeAuditEvent.ActionMaxLength);
            auditEvent.Property(e => e.Details).HasMaxLength(ScopeAuditEvent.DetailsMaxLength);
            auditEvent.Property(e => e.OccurredAtUtc).IsRequired();

            auditEvent.HasIndex(e => e.ScopeId);
            auditEvent.HasIndex(e => e.WorkspaceId);
            auditEvent.HasIndex(e => e.OccurredAtUtc);

            auditEvent.HasOne<AuthorizedScope>()
                .WithMany()
                .HasForeignKey(e => e.ScopeId)
                .OnDelete(DeleteBehavior.Cascade);

            auditEvent.HasOne<Workspace>()
                .WithMany()
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            auditEvent.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
