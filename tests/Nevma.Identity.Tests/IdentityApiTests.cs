using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nevma.Contracts.Identity;
using Nevma.Identity.Api.Infrastructure.Authentication;

namespace Nevma.Identity.Tests;

public sealed class IdentityApiTests
{
    [Fact]
    public async Task Register_creates_an_account_but_me_requires_a_token()
    {
        using var factory = new IdentityApiFactory();
        using var client = CreateClient(factory);
        await factory.InitializeDatabaseAsync();

        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            "maria@example.com",
            "Strong!Password123",
            "Maria",
            null));
        var me = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Login_page_contains_antiforgery_and_security_headers()
    {
        using var factory = new IdentityApiFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/account/login?returnUrl=%2Fconnect%2Fauthorize");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.Contains("form-action 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Token_endpoint_is_rate_limited()
    {
        using var factory = new IdentityApiFactory();
        using var client = CreateClient(factory);
        await factory.InitializeDatabaseAsync();
        HttpResponseMessage? response = null;

        for (var attempt = 0;
             attempt <= AuthenticationConfiguration.AuthenticationPermitLimit;
             attempt++)
        {
            response?.Dispose();
            response = await client.PostAsync(
                "/connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = "invalid"
                }));
        }

        using var finalResponse = response ?? throw new InvalidOperationException("No token response was received.");
        Assert.Equal(HttpStatusCode.TooManyRequests, finalResponse.StatusCode);
    }

    [Fact]
    public async Task Password_reset_request_does_not_disclose_unknown_accounts()
    {
        using var factory = new IdentityApiFactory();
        using var client = CreateClient(factory);
        await factory.InitializeDatabaseAsync();

        var response = await client.PostAsJsonAsync(
            "/api/auth/password-reset/request",
            new RequestPasswordResetRequest("missing@example.com"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_password_reset_token_is_rejected()
    {
        using var factory = new IdentityApiFactory();
        using var client = CreateClient(factory);
        await factory.InitializeDatabaseAsync();

        var response = await client.PostAsJsonAsync(
            "/api/auth/password-reset/confirm",
            new ResetPasswordRequest(
                "missing@example.com",
                "invalid-token",
                "NewStrong!Password123"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Mfa_endpoints_require_authentication_and_disable_secret_caching()
    {
        using var factory = new IdentityApiFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/auth/mfa");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_user_header_can_manage_address_book_contacts()
    {
        using var factory = new IdentityApiFactory();
        using var client = CreateClient(factory);
        await factory.InitializeDatabaseAsync();
        client.DefaultRequestHeaders.Add("X-Nevma-User-Id", Guid.NewGuid().ToString());

        var created = await client.PostAsJsonAsync(
            "/api/address-book/contacts/",
            new SaveAddressBookContactRequest("Konstantina Nika", "6982125235", "konstantina@example.com", null, "Ερατούς 9", null));
        var listed = await client.GetFromJsonAsync<AddressBookContactResponse[]>("/api/address-book/contacts/");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var contact = Assert.Single(listed!);
        Assert.Equal("Konstantina Nika", contact.Name);
        Assert.Equal("6982125235", contact.Phone);
    }

    private static HttpClient CreateClient(IdentityApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
}
