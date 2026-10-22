using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
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
    public async Task Password_login_returns_the_profile_name_for_the_account_menu()
    {
        using var factory = new IdentityApiFactory();
        using var client = CreateClient(factory);
        await factory.InitializeDatabaseAsync();
        await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            "profile-menu@example.com",
            "Strong!Password123",
            "Nektarios",
            null));

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "profile-menu@example.com",
            password = "Strong!Password123"
        });
        var session = await response.Content.ReadFromJsonAsync<ExternalLoginSession>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(session);
        Assert.Equal("Nektarios", session.DisplayName);
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
    public async Task External_provider_status_is_explicit_and_instagram_is_not_presented_as_authentication()
    {
        using var factory = new IdentityApiFactory();
        using var client = CreateClient(factory);

        var providers = await client.GetFromJsonAsync<ExternalProviderStatus[]>("/account/external/providers");

        Assert.NotNull(providers);
        Assert.Contains(providers, provider => provider.Provider == "Google" && !provider.IsConfigured);
        Assert.Contains(providers, provider => provider.Provider == "Facebook" && !provider.IsConfigured);
        var instagram = Assert.Single(providers, provider => provider.Provider == "Instagram");
        Assert.False(instagram.IsConfigured);
        Assert.Contains("Facebook", instagram.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unconfigured_external_provider_returns_service_unavailable()
    {
        using var factory = new IdentityApiFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/account/external/google?returnUrl=http%3A%2F%2Flocalhost%3A5173%2F");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task External_login_exchange_accepts_only_a_valid_short_lived_server_token()
    {
        using var factory = new IdentityApiFactory();
        using var client = CreateClient(factory);
        await factory.InitializeDatabaseAsync();
        var registration = await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            "external-session@example.com",
            "Strong!Password123",
            "External Session",
            null));
        var registeredUser = await registration.Content.ReadFromJsonAsync<UserSummary>();
        Assert.NotNull(registeredUser);
        var expectedUserId = registeredUser.Id;
        var dataProtectionProvider = factory.Services.GetRequiredService<IDataProtectionProvider>();
        var loginToken = dataProtectionProvider
            .CreateProtector("Nevma.Identity.ExternalLogin.v1")
            .ToTimeLimitedDataProtector()
            .Protect(expectedUserId.ToString("D"), TimeSpan.FromMinutes(1));

        var validResponse = await client.PostAsJsonAsync(
            "/account/external/exchange",
            new { loginToken });
        var session = await validResponse.Content.ReadFromJsonAsync<ExternalLoginSession>();
        var invalidResponse = await client.PostAsJsonAsync(
            "/account/external/exchange",
            new { loginToken = "not-a-valid-token" });

        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
        Assert.NotNull(session);
        Assert.True(session.Authenticated);
        Assert.Equal(expectedUserId, session.UserId);
        Assert.Equal("External Session", session.DisplayName);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
    }

    [Fact]
    public void Production_authentication_fails_closed_without_social_credentials()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddNevmaAuthentication(configuration, new ProductionEnvironment()));

        Assert.Contains("Google authentication credentials", exception.Message, StringComparison.Ordinal);
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

    private sealed record ExternalProviderStatus(string Provider, bool IsConfigured, string? Message);

    private sealed record ExternalLoginSession(bool Authenticated, Guid UserId, string DisplayName);

    private sealed class ProductionEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Nevma.Identity.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
