using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Nevma.Identity.Api.Infrastructure.Authentication;
using Nevma.Identity.Api.Infrastructure.Persistence;
using Nevma.Identity.Api.Application.Security;

namespace Nevma.Identity.Api.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", async (
            LoginRequest request,
            SignInManager<IdentityAccount> signInManager,
            SecurityEventService securityEvents,
            HttpContext context) =>
        {
            var account = await signInManager.UserManager.FindByEmailAsync(request.Email.Trim());
            var result = await signInManager.PasswordSignInAsync(
                request.Email.Trim(), request.Password, isPersistent: false, lockoutOnFailure: true);

            if (account is not null)
                await securityEvents.RecordFromRequestAsync(account.Id, "login.password", result.Succeeded, context);

            return result.Succeeded && account is not null
                ? Results.Ok(new { authenticated = true, userId = account.Id })
                : Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed.");
        }).AllowAnonymous().RequireRateLimiting(AuthenticationConfiguration.AuthenticationRateLimitPolicy);

        var account = endpoints.MapGroup("/account")
            .AllowAnonymous()
            .WithTags("Authentication");

        account.MapGet("/login", (HttpContext context, string? returnUrl) =>
        {
            SetSensitiveResponseHeaders(context.Response);
            var isDevelopment = context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();
            return Results.Content(BuildPasswordLoginPage(context, returnUrl, isDevelopment), "text/html; charset=utf-8");
        });

        account.MapPost("/login", async (
            [FromForm] LoginForm form,
            HttpContext context,
            SignInManager<IdentityAccount> signInManager,
            SecurityEventService securityEvents,
            IHostEnvironment environment) =>
        {
            SetSensitiveResponseHeaders(context.Response);
            IdentityAccount? account;
            string eventType;
            Microsoft.AspNetCore.Identity.SignInResult result;
            if (!string.IsNullOrWhiteSpace(form.RecoveryCode))
            {
                account = await signInManager.GetTwoFactorAuthenticationUserAsync();
                eventType = "login.recovery_code";
                result = await signInManager.TwoFactorRecoveryCodeSignInAsync(form.RecoveryCode.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(form.Code))
            {
                account = await signInManager.GetTwoFactorAuthenticationUserAsync();
                eventType = "login.mfa";
                result = await signInManager.TwoFactorAuthenticatorSignInAsync(
                    NormalizeCode(form.Code),
                    isPersistent: false,
                    rememberClient: false);
            }
            else
            {
                account = await signInManager.UserManager.FindByEmailAsync(form.Email.Trim());
                eventType = "login.password";
                result = await signInManager.PasswordSignInAsync(
                    form.Email.Trim(),
                    form.Password,
                    isPersistent: false,
                    lockoutOnFailure: true);
            }

            if (account is not null)
            {
                await securityEvents.RecordFromRequestAsync(
                    account.Id,
                    eventType,
                    result.Succeeded || result.RequiresTwoFactor,
                    context);
            }

            if (result.RequiresTwoFactor)
            {
                return Results.Content(
                    BuildTwoFactorLoginPage(context, form.ReturnUrl, environment.IsDevelopment()),
                    "text/html; charset=utf-8");
            }

            if (result.Succeeded)
            {
                var redirectUrl = GetSafeReturnUrl(form.ReturnUrl, environment.IsDevelopment());
                if (environment.IsDevelopment() && Uri.TryCreate(redirectUrl, UriKind.Absolute, out _))
                    redirectUrl += redirectUrl.Contains('?') ? "&signedIn=1" : "?signedIn=1";
                return Results.Redirect(redirectUrl);
            }

            return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Sign-in failed.",
                    detail: "The credentials are invalid or the account is temporarily unavailable.");
        })
        .RequireRateLimiting(AuthenticationConfiguration.AuthenticationRateLimitPolicy);

        return endpoints;
    }

    private static string BuildPasswordLoginPage(HttpContext context, string? returnUrl, bool isDevelopment) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Sign in to Nevma</title>
        </head>
        <body>
            <main>
                <h1>Sign in to Nevma</h1>
                <form method="post" action="/account/login">
                    <input type="hidden" name="__RequestVerificationToken" value="{{WebUtility.HtmlEncode(CreateAntiforgeryToken(context))}}">
                    <input type="hidden" name="ReturnUrl" value="{{WebUtility.HtmlEncode(GetSafeReturnUrl(returnUrl, isDevelopment))}}">
                    <label>Email <input name="Email" type="email" autocomplete="username" required></label>
                    <label>Password <input name="Password" type="password" autocomplete="current-password" required></label>
                    <button type="submit">Sign in</button>
                </form>
            </main>
        </body>
        </html>
        """;

    private static string BuildTwoFactorLoginPage(HttpContext context, string? returnUrl, bool isDevelopment) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Verify your Nevma sign-in</title>
        </head>
        <body>
            <main>
                <h1>Two-step verification</h1>
                <form method="post" action="/account/login">
                    <input type="hidden" name="__RequestVerificationToken" value="{{WebUtility.HtmlEncode(CreateAntiforgeryToken(context))}}">
                    <input type="hidden" name="ReturnUrl" value="{{WebUtility.HtmlEncode(GetSafeReturnUrl(returnUrl, isDevelopment))}}">
                    <label>Authenticator code <input name="Code" inputmode="numeric" autocomplete="one-time-code"></label>
                    <p>Or use one of your recovery codes.</p>
                    <label>Recovery code <input name="RecoveryCode" autocomplete="one-time-code"></label>
                    <button type="submit">Verify</button>
                </form>
            </main>
        </body>
        </html>
        """;

    private static string CreateAntiforgeryToken(HttpContext context) =>
        context.RequestServices.GetRequiredService<IAntiforgery>()
            .GetAndStoreTokens(context)
            .RequestToken
        ?? throw new InvalidOperationException("An antiforgery request token could not be created.");

    private static string NormalizeCode(string code) =>
        code.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

    private static void SetSensitiveResponseHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
    }

    private static string GetSafeReturnUrl(string? returnUrl, bool isDevelopment) =>
        isDevelopment &&
        Uri.TryCreate(returnUrl, UriKind.Absolute, out var absoluteUri) &&
        absoluteUri.Scheme == Uri.UriSchemeHttp &&
        (absoluteUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || absoluteUri.Host == "127.0.0.1") &&
        absoluteUri.Port == 5173
            ? absoluteUri.ToString()
            :
        !string.IsNullOrWhiteSpace(returnUrl) &&
        Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) &&
        returnUrl.StartsWith("/", StringComparison.Ordinal) &&
        !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}

public sealed class LoginForm
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string RecoveryCode { get; init; } = string.Empty;
    public string ReturnUrl { get; init; } = "/";
}

public sealed class LoginRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
