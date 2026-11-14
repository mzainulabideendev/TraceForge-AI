using System.Net;
using System.Text.Json;

namespace TraceForge.Api.Tests;

public sealed class WorkspaceApiTests : ApiTestBase
{
    private const int Owner = 1;
    private const int Administrator = 2;
    private const int Analyst = 3;

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return await ApiTestClient.ReadJsonAsync(response);
    }

    private static async Task<Guid> CreateWorkspaceAsync(ApiTestClient client, string name)
    {
        using var response = await client.PostJsonAsync("/api/workspaces", new { name, description = $"{name} description" });
        var json = await ReadAsync(response, HttpStatusCode.Created);
        return json.GetProperty("id").GetGuid();
    }

    private static async Task<(HttpStatusCode Status, JsonElement? Body)> AddMemberAsync(
        ApiTestClient client,
        Guid workspaceId,
        string email,
        int role)
    {
        using var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspaceId}/members",
            new { email, role = RoleName(role) });

        return response.StatusCode == HttpStatusCode.Created
            ? (response.StatusCode, await ApiTestClient.ReadJsonAsync(response))
            : (response.StatusCode, null);
    }

    private static async Task<JsonElement> GetMembersAsync(ApiTestClient client, Guid workspaceId)
    {
        using var response = await client.GetAsync($"/api/workspaces/{workspaceId}/members");
        return await ReadAsync(response, HttpStatusCode.OK);
    }

    private static async Task<Guid> FindUserIdAsync(ApiTestClient client, Guid workspaceId, string email)
    {
        var members = await GetMembersAsync(client, workspaceId);
        foreach (var member in members.EnumerateArray())
        {
            if (string.Equals(member.GetProperty("email").GetString(), email, StringComparison.OrdinalIgnoreCase))
            {
                return member.GetProperty("userId").GetGuid();
            }
        }

        throw new InvalidOperationException($"Member {email} was not found.");
    }

    private static string RoleName(int role) => role switch
    {
        Owner => "Owner",
        Administrator => "Administrator",
        _ => "Analyst",
    };

    [Fact]
    public async Task CreateWorkspace_MakesCallerOwner()
    {
        using var owner = new ApiTestClient(Factory);
        await owner.RegisterAndAuthenticateAsync("owner-create@example.test", "Owner Create");

        using var response = await owner.PostJsonAsync("/api/workspaces", new { name = "Alpha", description = "First" });
        var workspace = await ReadAsync(response, HttpStatusCode.Created);

        Assert.Equal("Alpha", workspace.GetProperty("name").GetString());
        Assert.Equal("Active", workspace.GetProperty("status").GetString());
        Assert.Equal(Owner, workspace.GetProperty("callerRole").GetInt32());
        Assert.Equal(1, workspace.GetProperty("memberCount").GetInt32());
        Assert.Equal(0, workspace.GetProperty("scopeCount").GetInt32());

        using var listResponse = await owner.GetAsync("/api/workspaces");
        var list = await ReadAsync(listResponse, HttpStatusCode.OK);
        Assert.Single(list.EnumerateArray());
        Assert.Equal(Owner, list[0].GetProperty("role").GetInt32());
    }

    [Fact]
    public async Task CreateWorkspace_RejectsInvalidName()
    {
        using var owner = new ApiTestClient(Factory);
        await owner.RegisterAndAuthenticateAsync("owner-invalid@example.test", "Owner Invalid");

        using var response = await owner.PostJsonAsync("/api/workspaces", new { name = " ", description = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NonMember_CannotDiscoverOrAccessWorkspace()
    {
        using var owner = new ApiTestClient(Factory);
        await owner.RegisterAndAuthenticateAsync("owner-isolation@example.test", "Owner Isolation");
        var workspaceId = await CreateWorkspaceAsync(owner, "Isolated");

        using var outsider = new ApiTestClient(Factory);
        await outsider.RegisterAndAuthenticateAsync("outsider@example.test", "Outsider");

        using var getResponse = await outsider.GetAsync($"/api/workspaces/{workspaceId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        using var updateResponse = await outsider.PutJsonAsync($"/api/workspaces/{workspaceId}", new { name = "Hijacked", description = "" });
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);

        using var membersResponse = await outsider.GetAsync($"/api/workspaces/{workspaceId}/members");
        Assert.Equal(HttpStatusCode.NotFound, membersResponse.StatusCode);

        using var listResponse = await outsider.GetAsync("/api/workspaces");
        var list = await ReadAsync(listResponse, HttpStatusCode.OK);
        Assert.Empty(list.EnumerateArray());
    }

    [Fact]
    public async Task Analyst_CanReadButNotManage()
    {
        using var owner = new ApiTestClient(Factory);
        await owner.RegisterAndAuthenticateAsync("owner-analyst@example.test", "Owner Analyst");
        var workspaceId = await CreateWorkspaceAsync(owner, "Analyst Workspace");

        using var analyst = new ApiTestClient(Factory);
        await analyst.RegisterAndAuthenticateAsync("analyst-only@example.test", "Analyst Only");
        Assert.Equal(HttpStatusCode.Created, (await AddMemberAsync(owner, workspaceId, "analyst-only@example.test", Analyst)).Status);

        using var getResponse = await analyst.GetAsync($"/api/workspaces/{workspaceId}");
        await ReadAsync(getResponse, HttpStatusCode.OK);

        using var membersResponse = await analyst.GetAsync($"/api/workspaces/{workspaceId}/members");
        await ReadAsync(membersResponse, HttpStatusCode.OK);

        using var updateResponse = await analyst.PutJsonAsync($"/api/workspaces/{workspaceId}", new { name = "Nope", description = "" });
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);

        using var archiveResponse = await analyst.PostJsonAsync($"/api/workspaces/{workspaceId}/archive", new { });
        Assert.Equal(HttpStatusCode.Forbidden, archiveResponse.StatusCode);

        using var addResponse = await analyst.PostJsonAsync(
            $"/api/workspaces/{workspaceId}/members",
            new { email = "someone@example.test", role = "Analyst" });
        Assert.Equal(HttpStatusCode.Forbidden, addResponse.StatusCode);
    }

    [Fact]
    public async Task Administrator_CanUpdateButCannotArchiveOrGrantOwner()
    {
        using var owner = new ApiTestClient(Factory);
        await owner.RegisterAndAuthenticateAsync("owner-admin@example.test", "Owner Admin");
        var workspaceId = await CreateWorkspaceAsync(owner, "Admin Workspace");

        using var admin = new ApiTestClient(Factory);
        await admin.RegisterAndAuthenticateAsync("administrator@example.test", "Administrator User");
        Assert.Equal(HttpStatusCode.Created, (await AddMemberAsync(owner, workspaceId, "administrator@example.test", Administrator)).Status);

        using var updateResponse = await admin.PutJsonAsync(
            $"/api/workspaces/{workspaceId}",
            new { name = "Admin Renamed", description = "updated" });
        await ReadAsync(updateResponse, HttpStatusCode.OK);

        using var archiveResponse = await admin.PostJsonAsync($"/api/workspaces/{workspaceId}/archive", new { });
        Assert.Equal(HttpStatusCode.Forbidden, archiveResponse.StatusCode);

        using (var grantOwnerResponse = await admin.PostJsonAsync(
                   $"/api/workspaces/{workspaceId}/members",
                   new { email = "new-owner@example.test", role = "Owner" }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, grantOwnerResponse.StatusCode);
        }

        var ownerId = await FindUserIdAsync(owner, workspaceId, "owner-admin@example.test");
        using var promoteOwnerResponse = await admin.PatchJsonAsync(
            $"/api/workspaces/{workspaceId}/members/{ownerId}",
            new { role = "Owner" });
        Assert.Equal(HttpStatusCode.Forbidden, promoteOwnerResponse.StatusCode);

        // An administrator also cannot demote or remove an owner.
        using var demoteResponse = await admin.PatchJsonAsync(
            $"/api/workspaces/{workspaceId}/members/{ownerId}",
            new { role = "Analyst" });
        Assert.Equal(HttpStatusCode.Forbidden, demoteResponse.StatusCode);

        using var removeResponse = await admin.DeleteAsync($"/api/workspaces/{workspaceId}/members/{ownerId}");
        Assert.Equal(HttpStatusCode.Forbidden, removeResponse.StatusCode);
    }

    [Fact]
    public async Task LastOwner_CannotBeDemotedOrRemoved()
    {
        using var owner = new ApiTestClient(Factory);
        await owner.RegisterAndAuthenticateAsync("owner-last@example.test", "Owner Last");
        var workspaceId = await CreateWorkspaceAsync(owner, "Owner Protection");
        var ownerId = await FindUserIdAsync(owner, workspaceId, "owner-last@example.test");

        using var demoteResponse = await owner.PatchJsonAsync(
            $"/api/workspaces/{workspaceId}/members/{ownerId}",
            new { role = "Administrator" });
        Assert.Equal(HttpStatusCode.Conflict, demoteResponse.StatusCode);

        using var removeResponse = await owner.DeleteAsync($"/api/workspaces/{workspaceId}/members/{ownerId}");
        Assert.Equal(HttpStatusCode.Conflict, removeResponse.StatusCode);

        // The last owner still exists.
        var members = await GetMembersAsync(owner, workspaceId);
        Assert.Single(members.EnumerateArray());
    }

    [Fact]
    public async Task Owner_CanGrantOwnershipThenLeave()
    {
        using var firstOwner = new ApiTestClient(Factory);
        await firstOwner.RegisterAndAuthenticateAsync("owner-first@example.test", "Owner First");
        var workspaceId = await CreateWorkspaceAsync(firstOwner, "Transfer");

        using var secondOwner = new ApiTestClient(Factory);
        await secondOwner.RegisterAndAuthenticateAsync("owner-second@example.test", "Owner Second");
        Assert.Equal(HttpStatusCode.Created, (await AddMemberAsync(firstOwner, workspaceId, "owner-second@example.test", Owner)).Status);

        var firstOwnerId = await FindUserIdAsync(firstOwner, workspaceId, "owner-first@example.test");
        using var leaveResponse = await firstOwner.DeleteAsync($"/api/workspaces/{workspaceId}/members/{firstOwnerId}");
        Assert.Equal(HttpStatusCode.NoContent, leaveResponse.StatusCode);

        using var goneResponse = await firstOwner.GetAsync($"/api/workspaces/{workspaceId}");
        Assert.Equal(HttpStatusCode.NotFound, goneResponse.StatusCode);

        var members = await GetMembersAsync(secondOwner, workspaceId);
        Assert.Single(members.EnumerateArray());
    }

    [Fact]
    public async Task AddMember_ValidatesExistenceAndDuplicates()
    {
        using var owner = new ApiTestClient(Factory);
        await owner.RegisterAndAuthenticateAsync("owner-members@example.test", "Owner Members");
        var workspaceId = await CreateWorkspaceAsync(owner, "Membership");

        using var member = new ApiTestClient(Factory);
        await member.RegisterAndAuthenticateAsync("member-duplicate@example.test", "Member Duplicate");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await AddMemberAsync(owner, workspaceId, "missing@example.test", Analyst)).Status);

        Assert.Equal(
            HttpStatusCode.Created,
            (await AddMemberAsync(owner, workspaceId, "member-duplicate@example.test", Analyst)).Status);

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await AddMemberAsync(owner, workspaceId, "member-duplicate@example.test", Administrator)).Status);
    }

    [Fact]
    public async Task ArchivedWorkspace_RejectsMutationsButAllowsReads()
    {
        using var owner = new ApiTestClient(Factory);
        await owner.RegisterAndAuthenticateAsync("owner-archive@example.test", "Owner Archive");
        var workspaceId = await CreateWorkspaceAsync(owner, "Archive Me");

        using var archiveResponse = await owner.PostJsonAsync($"/api/workspaces/{workspaceId}/archive", new { });
        var archived = await ReadAsync(archiveResponse, HttpStatusCode.OK);
        Assert.Equal("Archived", archived.GetProperty("status").GetString());
        Assert.False(archived.GetProperty("archivedAtUtc").ValueKind == JsonValueKind.Null);

        using var getResponse = await owner.GetAsync($"/api/workspaces/{workspaceId}");
        await ReadAsync(getResponse, HttpStatusCode.OK);

        using var membersResponse = await owner.GetAsync($"/api/workspaces/{workspaceId}/members");
        await ReadAsync(membersResponse, HttpStatusCode.OK);

        using var updateResponse = await owner.PutJsonAsync($"/api/workspaces/{workspaceId}", new { name = "Still archived", description = "" });
        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);

        using var scopeResponse = await owner.PostJsonAsync(
            $"/api/workspaces/{workspaceId}/scopes",
            new { type = "IPv4", target = "8.8.8.8" });
        Assert.Equal(HttpStatusCode.Conflict, scopeResponse.StatusCode);
    }

    [Fact]
    public async Task WorkspaceChanges_AreAudited()
    {
        using var owner = new ApiTestClient(Factory);
        await owner.RegisterAndAuthenticateAsync("owner-audit@example.test", "Owner Audit");
        var workspaceId = await CreateWorkspaceAsync(owner, "Audited");

        using var member = new ApiTestClient(Factory);
        await member.RegisterAndAuthenticateAsync("member-audit@example.test", "Member Audit");
        Assert.Equal(HttpStatusCode.Created, (await AddMemberAsync(owner, workspaceId, "member-audit@example.test", Analyst)).Status);

        var memberId = await FindUserIdAsync(owner, workspaceId, "member-audit@example.test");
        using var roleResponse = await owner.PatchJsonAsync(
            $"/api/workspaces/{workspaceId}/members/{memberId}",
            new { role = "Administrator" });
        await ReadAsync(roleResponse, HttpStatusCode.OK);

        using var removeResponse = await owner.DeleteAsync($"/api/workspaces/{workspaceId}/members/{memberId}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        using var archiveResponse = await owner.PostJsonAsync($"/api/workspaces/{workspaceId}/archive", new { });
        await ReadAsync(archiveResponse, HttpStatusCode.OK);

        using var auditResponse = await owner.GetAsync("/api/admin/audit?page=1&pageSize=100");
        var audit = await ReadAsync(auditResponse, HttpStatusCode.OK);

        var actions = audit.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("action").GetString())
            .ToArray();

        Assert.Contains("workspace.created", actions);
        Assert.Contains("workspace.member.added", actions);
        Assert.Contains("workspace.member.role.changed", actions);
        Assert.Contains("workspace.member.removed", actions);
        Assert.Contains("workspace.archived", actions);

        var details = audit.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("details").GetString())
            .Where(value => value is not null)
            .ToArray();
        Assert.DoesNotContain(details, value => value!.Contains("password", StringComparison.OrdinalIgnoreCase));
    }
}
