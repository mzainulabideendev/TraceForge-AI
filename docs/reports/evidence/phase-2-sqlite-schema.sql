CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "AspNetRoles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AspNetRoles" PRIMARY KEY,
    "Name" TEXT NULL,
    "NormalizedName" TEXT NULL,
    "ConcurrencyStamp" TEXT NULL
);

CREATE TABLE "AspNetUsers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AspNetUsers" PRIMARY KEY,
    "DisplayName" TEXT NOT NULL,
    "CreatedAtUtc" INTEGER NOT NULL,
    "LastLoginAtUtc" INTEGER NULL,
    "UserName" TEXT NULL,
    "NormalizedUserName" TEXT NULL,
    "Email" TEXT NULL,
    "NormalizedEmail" TEXT NULL,
    "EmailConfirmed" INTEGER NOT NULL,
    "PasswordHash" TEXT NULL,
    "SecurityStamp" TEXT NULL,
    "ConcurrencyStamp" TEXT NULL,
    "PhoneNumber" TEXT NULL,
    "PhoneNumberConfirmed" INTEGER NOT NULL,
    "TwoFactorEnabled" INTEGER NOT NULL,
    "LockoutEnd" INTEGER NULL,
    "LockoutEnabled" INTEGER NOT NULL,
    "AccessFailedCount" INTEGER NOT NULL
);

CREATE TABLE "AuditLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AuditLogs" PRIMARY KEY AUTOINCREMENT,
    "ActorUserId" TEXT NULL,
    "ActorIdentifier" TEXT NULL,
    "Action" TEXT NOT NULL,
    "TargetType" TEXT NOT NULL,
    "TargetId" TEXT NULL,
    "Outcome" TEXT NOT NULL,
    "IpAddress" TEXT NULL,
    "UserAgent" TEXT NULL,
    "Details" TEXT NULL,
    "OccurredAtUtc" INTEGER NOT NULL
);

CREATE TABLE "AspNetRoleClaims" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AspNetRoleClaims" PRIMARY KEY AUTOINCREMENT,
    "RoleId" TEXT NOT NULL,
    "ClaimType" TEXT NULL,
    "ClaimValue" TEXT NULL,
    CONSTRAINT "FK_AspNetRoleClaims_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AspNetUserClaims" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AspNetUserClaims" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "ClaimType" TEXT NULL,
    "ClaimValue" TEXT NULL,
    CONSTRAINT "FK_AspNetUserClaims_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AspNetUserLogins" (
    "LoginProvider" TEXT NOT NULL,
    "ProviderKey" TEXT NOT NULL,
    "ProviderDisplayName" TEXT NULL,
    "UserId" TEXT NOT NULL,
    CONSTRAINT "PK_AspNetUserLogins" PRIMARY KEY ("LoginProvider", "ProviderKey"),
    CONSTRAINT "FK_AspNetUserLogins_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AspNetUserRoles" (
    "UserId" TEXT NOT NULL,
    "RoleId" TEXT NOT NULL,
    CONSTRAINT "PK_AspNetUserRoles" PRIMARY KEY ("UserId", "RoleId"),
    CONSTRAINT "FK_AspNetUserRoles_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AspNetUserRoles_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AspNetUserTokens" (
    "UserId" TEXT NOT NULL,
    "LoginProvider" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Value" TEXT NULL,
    CONSTRAINT "PK_AspNetUserTokens" PRIMARY KEY ("UserId", "LoginProvider", "Name"),
    CONSTRAINT "FK_AspNetUserTokens_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_AspNetRoleClaims_RoleId" ON "AspNetRoleClaims" ("RoleId");

CREATE UNIQUE INDEX "RoleNameIndex" ON "AspNetRoles" ("NormalizedName");

CREATE INDEX "IX_AspNetUserClaims_UserId" ON "AspNetUserClaims" ("UserId");

CREATE INDEX "IX_AspNetUserLogins_UserId" ON "AspNetUserLogins" ("UserId");

CREATE INDEX "IX_AspNetUserRoles_RoleId" ON "AspNetUserRoles" ("RoleId");

CREATE INDEX "EmailIndex" ON "AspNetUsers" ("NormalizedEmail");

CREATE INDEX "IX_AspNetUsers_CreatedAtUtc" ON "AspNetUsers" ("CreatedAtUtc");

CREATE UNIQUE INDEX "UserNameIndex" ON "AspNetUsers" ("NormalizedUserName");

CREATE INDEX "IX_AuditLogs_Action" ON "AuditLogs" ("Action");

CREATE INDEX "IX_AuditLogs_ActorUserId" ON "AuditLogs" ("ActorUserId");

CREATE INDEX "IX_AuditLogs_OccurredAtUtc" ON "AuditLogs" ("OccurredAtUtc");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261114100917_InitialCreate', '10.0.15');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "Workspaces" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Workspaces" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Description" TEXT NULL,
    "Status" TEXT NOT NULL,
    "CreatedByUserId" TEXT NOT NULL,
    "CreatedAtUtc" INTEGER NOT NULL,
    "UpdatedAtUtc" INTEGER NOT NULL,
    "ArchivedAtUtc" INTEGER NULL,
    CONSTRAINT "FK_Workspaces_AspNetUsers_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AuthorizedScopes" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AuthorizedScopes" PRIMARY KEY,
    "WorkspaceId" TEXT NOT NULL,
    "Type" TEXT NOT NULL,
    "NormalizedTarget" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "ApprovalReference" TEXT NULL,
    "ApprovedByUserId" TEXT NULL,
    "ApprovedAtUtc" INTEGER NULL,
    "RevokedByUserId" TEXT NULL,
    "RevokedAtUtc" INTEGER NULL,
    "ValidFromUtc" INTEGER NOT NULL,
    "ExpiresAtUtc" INTEGER NOT NULL,
    "Restrictions" TEXT NOT NULL,
    "CreatedByUserId" TEXT NOT NULL,
    "CreatedAtUtc" INTEGER NOT NULL,
    "UpdatedAtUtc" INTEGER NOT NULL,
    CONSTRAINT "FK_AuthorizedScopes_AspNetUsers_ApprovedByUserId" FOREIGN KEY ("ApprovedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AuthorizedScopes_AspNetUsers_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AuthorizedScopes_AspNetUsers_RevokedByUserId" FOREIGN KEY ("RevokedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AuthorizedScopes_Workspaces_WorkspaceId" FOREIGN KEY ("WorkspaceId") REFERENCES "Workspaces" ("Id") ON DELETE CASCADE
);

CREATE TABLE "WorkspaceMemberships" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_WorkspaceMemberships" PRIMARY KEY,
    "WorkspaceId" TEXT NOT NULL,
    "UserId" TEXT NOT NULL,
    "Role" TEXT NOT NULL,
    "CreatedAtUtc" INTEGER NOT NULL,
    "UpdatedAtUtc" INTEGER NOT NULL,
    CONSTRAINT "FK_WorkspaceMemberships_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_WorkspaceMemberships_Workspaces_WorkspaceId" FOREIGN KEY ("WorkspaceId") REFERENCES "Workspaces" ("Id") ON DELETE CASCADE
);

CREATE TABLE "ScopeApprovals" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ScopeApprovals" PRIMARY KEY AUTOINCREMENT,
    "WorkspaceId" TEXT NOT NULL,
    "ScopeId" TEXT NOT NULL,
    "ApprovedByUserId" TEXT NOT NULL,
    "Reference" TEXT NOT NULL,
    "ApprovedAtUtc" INTEGER NOT NULL,
    CONSTRAINT "FK_ScopeApprovals_AspNetUsers_ApprovedByUserId" FOREIGN KEY ("ApprovedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ScopeApprovals_AuthorizedScopes_ScopeId" FOREIGN KEY ("ScopeId") REFERENCES "AuthorizedScopes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ScopeApprovals_Workspaces_WorkspaceId" FOREIGN KEY ("WorkspaceId") REFERENCES "Workspaces" ("Id") ON DELETE CASCADE
);

CREATE TABLE "ScopeAuditEvents" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ScopeAuditEvents" PRIMARY KEY AUTOINCREMENT,
    "WorkspaceId" TEXT NOT NULL,
    "ScopeId" TEXT NOT NULL,
    "Action" TEXT NOT NULL,
    "ActorUserId" TEXT NOT NULL,
    "Details" TEXT NULL,
    "OccurredAtUtc" INTEGER NOT NULL,
    CONSTRAINT "FK_ScopeAuditEvents_AspNetUsers_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ScopeAuditEvents_AuthorizedScopes_ScopeId" FOREIGN KEY ("ScopeId") REFERENCES "AuthorizedScopes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ScopeAuditEvents_Workspaces_WorkspaceId" FOREIGN KEY ("WorkspaceId") REFERENCES "Workspaces" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_AuthorizedScopes_ApprovedByUserId" ON "AuthorizedScopes" ("ApprovedByUserId");

CREATE INDEX "IX_AuthorizedScopes_CreatedByUserId" ON "AuthorizedScopes" ("CreatedByUserId");

CREATE INDEX "IX_AuthorizedScopes_ExpiresAtUtc" ON "AuthorizedScopes" ("ExpiresAtUtc");

CREATE INDEX "IX_AuthorizedScopes_RevokedByUserId" ON "AuthorizedScopes" ("RevokedByUserId");

CREATE INDEX "IX_AuthorizedScopes_WorkspaceId_Status" ON "AuthorizedScopes" ("WorkspaceId", "Status");

CREATE UNIQUE INDEX "IX_AuthorizedScopes_WorkspaceId_Type_NormalizedTarget" ON "AuthorizedScopes" ("WorkspaceId", "Type", "NormalizedTarget");

CREATE INDEX "IX_ScopeApprovals_ApprovedByUserId" ON "ScopeApprovals" ("ApprovedByUserId");

CREATE INDEX "IX_ScopeApprovals_ScopeId" ON "ScopeApprovals" ("ScopeId");

CREATE INDEX "IX_ScopeApprovals_WorkspaceId" ON "ScopeApprovals" ("WorkspaceId");

CREATE INDEX "IX_ScopeAuditEvents_ActorUserId" ON "ScopeAuditEvents" ("ActorUserId");

CREATE INDEX "IX_ScopeAuditEvents_OccurredAtUtc" ON "ScopeAuditEvents" ("OccurredAtUtc");

CREATE INDEX "IX_ScopeAuditEvents_ScopeId" ON "ScopeAuditEvents" ("ScopeId");

CREATE INDEX "IX_ScopeAuditEvents_WorkspaceId" ON "ScopeAuditEvents" ("WorkspaceId");

CREATE INDEX "IX_WorkspaceMemberships_UserId" ON "WorkspaceMemberships" ("UserId");

CREATE UNIQUE INDEX "IX_WorkspaceMemberships_WorkspaceId_UserId" ON "WorkspaceMemberships" ("WorkspaceId", "UserId");

CREATE INDEX "IX_Workspaces_CreatedAtUtc" ON "Workspaces" ("CreatedAtUtc");

CREATE INDEX "IX_Workspaces_CreatedByUserId" ON "Workspaces" ("CreatedByUserId");

CREATE INDEX "IX_Workspaces_Status" ON "Workspaces" ("Status");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261114110000_Phase2Workspaces', '10.0.15');

COMMIT;

