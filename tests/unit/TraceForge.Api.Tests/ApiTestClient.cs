using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TraceForge.Api.Tests;

/// <summary>
/// Minimal client that behaves like the SPA: keeps the cookie container, fetches antiforgery
/// tokens before unsafe requests, and refreshes them after authentication changes.
/// </summary>
internal sealed class ApiTestClient : IDisposable
{
    public const string StrongPassword = "CorrectHorseBattery1";

    private readonly HttpClient _client;
    private string? _csrfToken;

    public ApiTestClient(TraceForgeApiFactory factory, Uri? baseAddress = null)
    {
        var options = new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        };

        if (baseAddress is not null)
        {
            options.BaseAddress = baseAddress;
        }

        _client = factory.CreateClient(options);
    }

    public HttpClient Http => _client;

    public async Task<string> FetchCsrfTokenAsync()
    {
        using var response = await _client.GetAsync(new Uri("/api/auth/csrf", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        _csrfToken = document.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("The antiforgery endpoint returned no token.");

        return _csrfToken;
    }

    public async Task<HttpResponseMessage> GetAsync(string path)
    {
        return await _client.GetAsync(new Uri(path, UriKind.Relative));
    }

    public async Task<HttpResponseMessage> PostJsonAsync(string path, object payload, bool includeCsrfToken = true)
    {
        return await SendJsonAsync(HttpMethod.Post, path, payload, includeCsrfToken);
    }

    public Task<HttpResponseMessage> PutJsonAsync(string path, object payload, bool includeCsrfToken = true) =>
        SendJsonAsync(HttpMethod.Put, path, payload, includeCsrfToken);

    public Task<HttpResponseMessage> PatchJsonAsync(string path, object payload, bool includeCsrfToken = true) =>
        SendJsonAsync(HttpMethod.Patch, path, payload, includeCsrfToken);

    public async Task<HttpResponseMessage> DeleteAsync(string path, bool includeCsrfToken = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(path, UriKind.Relative));

        if (includeCsrfToken)
        {
            _csrfToken ??= await FetchCsrfTokenAsync();
            request.Headers.Add("X-XSRF-TOKEN", _csrfToken);
        }

        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method,
        string path,
        object payload,
        bool includeCsrfToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(payload),
        };

        if (includeCsrfToken)
        {
            _csrfToken ??= await FetchCsrfTokenAsync();
            request.Headers.Add("X-XSRF-TOKEN", _csrfToken);
        }

        return await _client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> PostAsync(string path, bool includeCsrfToken = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));

        if (includeCsrfToken)
        {
            _csrfToken ??= await FetchCsrfTokenAsync();
            request.Headers.Add("X-XSRF-TOKEN", _csrfToken);
        }

        return await _client.SendAsync(request);
    }

    public Task<HttpResponseMessage> RegisterAsync(string email, string displayName, string? password = null)
    {
        return PostJsonAsync("/api/auth/register", new
        {
            email,
            password = password ?? StrongPassword,
            displayName,
        });
    }

    public Task<HttpResponseMessage> LoginAsync(string email, string password)
    {
        return PostJsonAsync("/api/auth/login", new { email, password });
    }

    /// <summary>Registers an account and leaves the client authenticated as it.</summary>
    public async Task RegisterAndAuthenticateAsync(string email, string displayName)
    {
        using var response = await RegisterAsync(email, displayName);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);

        // Authentication changed the identity bound to the antiforgery token.
        await FetchCsrfTokenAsync();
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    public void Dispose() => _client.Dispose();
}
