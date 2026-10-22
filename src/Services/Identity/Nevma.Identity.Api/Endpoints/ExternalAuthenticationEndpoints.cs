using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Nevma.Identity.Api.Domain.Users;
using Nevma.Identity.Api.Infrastructure.Authentication;
using Nevma.Identity.Api.Infrastructure.Persistence;

namespace Nevma.Identity.Api.Endpoints;

public static class ExternalAuthenticationEndpoints
{
    private const string ExternalLoginProtectorPurpose = "Nevma.Identity.ExternalLogin.v1";
    private static readonly TimeSpan ExternalLoginLifetime = TimeSpan.FromMinutes(2);

    public static IEndpointRouteBuilder MapExternalAuthenticationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var account = endpoints.MapGroup("/account")
            .AllowAnonymous()
            .WithTags("Authentication");

        account.MapGet("/external/providers", (IConfiguration configuration) => Results.Ok(new[]
        {
            new ExternalProviderResponse("Google", IsConfigured("Google", configuration), null),
            new ExternalProviderResponse("Facebook", IsConfigured("Facebook", configuration), null),
            new ExternalProviderResponse(
                "Instagram",
                false,
                "Το Instagram δεν προσφέρει γενικό sign in για εφαρμογές. Χρησιμοποίησε το Facebook / Meta sign in.")
        }));

        account.MapGet("/external/{provider}", (
            string provider,
            string? returnUrl,
            IConfiguration configuration,
            IHostEnvironment environment) =>
        {
            var scheme = NormalizeProvider(provider);
            if (scheme is null || !IsConfigured(scheme, configuration))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: $"{provider} sign in is not configured.",
                    detail: "Configure the provider credentials in the Identity service environment.");
            }

            var safeReturnUrl = NormalizeReturnUrl(returnUrl, configuration, environment);
            var callback = $"/account/external-callback?returnUrl={Uri.EscapeDataString(safeReturnUrl)}";
            return Results.Challenge(new AuthenticationProperties { RedirectUri = callback }, [scheme]);
        });

        account.MapGet("/external-callback", async (
            HttpContext context,
            string? returnUrl,
            UserManager<IdentityAccount> userManager,
            SignInManager<IdentityAccount> signInManager,
            IdentityDbContext dbContext,
            TimeProvider timeProvider,
            IDataProtectionProvider dataProtectionProvider,
            IConfiguration configuration,
            IHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            var external = await context.AuthenticateAsync(IdentityConstants.ExternalScheme);
            if (!external.Succeeded || external.Principal is null)
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "External sign in failed.");

            var provider = external.Principal.FindFirstValue(ClaimTypes.AuthenticationMethod);
            if (provider is null && external.Properties?.Items.TryGetValue(".AuthScheme", out var authScheme) == true)
                provider = authScheme;
            var providerKey = external.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = external.Principal.FindFirstValue(ClaimTypes.Email)
                ?? external.Principal.FindFirstValue("email");
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(email))
                return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Provider did not return a usable account.");

            var account = await userManager.FindByLoginAsync(provider, providerKey);
            if (account is null)
            {
                account = await userManager.FindByEmailAsync(email);
                if (account is null)
                {
                    account = new IdentityAccount
                    {
                        Id = Guid.NewGuid(),
                        Email = email,
                        UserName = email,
                        EmailConfirmed = true
                    };
                    var createResult = await userManager.CreateAsync(account);
                    if (!createResult.Succeeded)
                        return Results.ValidationProblem(createResult.Errors
                            .GroupBy(error => error.Code)
                            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));

                    var displayName = external.Principal.FindFirstValue(ClaimTypes.Name) ?? email.Split('@')[0];
                    await dbContext.Profiles.AddAsync(
                        User.Create(account.Id, displayName, null, timeProvider.GetUtcNow()),
                        cancellationToken);
                }

                var loginResult = await userManager.AddLoginAsync(
                    account,
                    new UserLoginInfo(provider, providerKey, provider));
                if (!loginResult.Succeeded)
                    return Results.ValidationProblem(loginResult.Errors
                        .GroupBy(error => error.Code)
                        .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await signInManager.SignInAsync(account, isPersistent: false);
            await context.SignOutAsync(IdentityConstants.ExternalScheme);
            var destination = NormalizeReturnUrl(returnUrl, configuration, environment);
            var loginToken = dataProtectionProvider
                .CreateProtector(ExternalLoginProtectorPurpose)
                .ToTimeLimitedDataProtector()
                .Protect(account.Id.ToString("D"), ExternalLoginLifetime);
            destination = QueryHelpers.AddQueryString(destination, new Dictionary<string, string?>
            {
                ["externalLogin"] = loginToken
            });
            return Results.Redirect(destination);
        });

        account.MapPost("/external/exchange", (
            ExternalLoginExchangeRequest request,
            IDataProtectionProvider dataProtectionProvider) =>
        {
            if (string.IsNullOrWhiteSpace(request.LoginToken))
                return Results.Unauthorized();

            try
            {
                var userIdValue = dataProtectionProvider
                    .CreateProtector(ExternalLoginProtectorPurpose)
                    .ToTimeLimitedDataProtector()
                    .Unprotect(request.LoginToken);
                return Guid.TryParse(userIdValue, out var userId)
                    ? Results.Ok(new { authenticated = true, userId })
                    : Results.Unauthorized();
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return Results.Unauthorized();
            }
        })
        .RequireRateLimiting(AuthenticationConfiguration.AuthenticationRateLimitPolicy);

        return endpoints;
    }

    private static string? NormalizeProvider(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "google" => "Google",
        "facebook" => "Facebook",
        _ => null
    };

    private static bool IsConfigured(string scheme, IConfiguration configuration) => scheme switch
    {
        "Google" => !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]) && !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]),
        "Facebook" => !string.IsNullOrWhiteSpace(configuration["Authentication:Facebook:AppId"]) && !string.IsNullOrWhiteSpace(configuration["Authentication:Facebook:AppSecret"]),
        _ => false
    };

    private static string NormalizeReturnUrl(
        string? returnUrl,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            if (returnUrl.StartsWith("/", StringComparison.Ordinal) &&
                !returnUrl.StartsWith("//", StringComparison.Ordinal))
                return returnUrl;

            if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var requested) &&
                configuration.GetSection("Authentication:AllowedExternalReturnUrls").Get<string[]>()
                    ?.Any(allowed => IsSameReturnDestination(requested, allowed)) == true)
                return returnUrl;
        }

        var configuredFallback = configuration
            .GetSection("Authentication:AllowedExternalReturnUrls")
            .Get<string[]>()
            ?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(configuredFallback))
            return configuredFallback;

        return environment.IsDevelopment() ? "http://localhost:5173/" : "/account/login";
    }

    private static bool IsSameReturnDestination(Uri requested, string allowed) =>
        Uri.TryCreate(allowed, UriKind.Absolute, out var allowedUri) &&
        string.Equals(requested.Scheme, allowedUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(requested.Host, allowedUri.Host, StringComparison.OrdinalIgnoreCase) &&
        requested.Port == allowedUri.Port &&
        string.Equals(requested.AbsolutePath.TrimEnd('/'), allowedUri.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private sealed record ExternalProviderResponse(string Provider, bool IsConfigured, string? Message);

    public sealed record ExternalLoginExchangeRequest(string LoginToken);
}
