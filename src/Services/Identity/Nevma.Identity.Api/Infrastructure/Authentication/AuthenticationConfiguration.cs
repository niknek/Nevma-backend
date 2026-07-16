using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Nevma.Identity.Api.Infrastructure.Persistence;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace Nevma.Identity.Api.Infrastructure.Authentication;

public static class AuthenticationConfiguration
{
    public const string AuthenticationRateLimitPolicy = "authentication";
    public const int AuthenticationPermitLimit = 10;

    public static IServiceCollection AddNevmaAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services
            .AddIdentityCore<IdentityAccount>(ConfigureIdentity)
            .AddRoles<IdentityRole<Guid>>()
            .AddSignInManager()
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddDefaultTokenProviders();

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            })
            .AddIdentityCookies(options =>
            {
                if (options.ApplicationCookie is null)
                    throw new InvalidOperationException("The Identity application cookie is not registered.");

                options.ApplicationCookie.Configure(cookie =>
                {
                    cookie.Cookie.Name = "__Host-Nevma.Identity";
                    cookie.Cookie.HttpOnly = true;
                    cookie.Cookie.SameSite = SameSiteMode.Lax;
                    cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    cookie.LoginPath = "/account/login";
                    cookie.LogoutPath = "/connect/logout";
                    cookie.SlidingExpiration = false;
                    cookie.ExpireTimeSpan = TimeSpan.FromMinutes(15);
                });
            });

        AddExternalProviders(services, configuration);

        services.AddAuthorization();
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "__Host-Nevma.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.StartsWithSegments("/connect/token")
                    ? CreateAuthenticationPartition(context)
                    : RateLimitPartition.GetNoLimiter("unlimited"));
            options.AddPolicy(AuthenticationRateLimitPolicy, context =>
                CreateAuthenticationPartition(context));
        });

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<IdentityDbContext>();
            })
            .AddServer(options =>
            {
                options
                    .SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetEndSessionEndpointUris("/connect/logout");

                options.AllowAuthorizationCodeFlow();
                options.AllowRefreshTokenFlow();
                options.RequireProofKeyForCodeExchange();
                options.RegisterScopes(
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    "nevma_api");
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(10));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(30));
                options.UseReferenceRefreshTokens();
                options.DisableAccessTokenEncryption();

                ConfigureServerCertificates(options, configuration, environment);

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
                options.EnableTokenEntryValidation();
            });

        return services;
    }

    private static void AddExternalProviders(IServiceCollection services, IConfiguration configuration)
    {
        var googleClientId = configuration["Authentication:Google:ClientId"];
        var googleClientSecret = configuration["Authentication:Google:ClientSecret"];
        if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
        {
            services.AddAuthentication().AddGoogle("Google", options =>
            {
                options.ClientId = googleClientId;
                options.ClientSecret = googleClientSecret;
                options.SignInScheme = IdentityConstants.ExternalScheme;
            });
        }

        var facebookAppId = configuration["Authentication:Facebook:AppId"];
        var facebookAppSecret = configuration["Authentication:Facebook:AppSecret"];
        if (!string.IsNullOrWhiteSpace(facebookAppId) && !string.IsNullOrWhiteSpace(facebookAppSecret))
        {
            services.AddAuthentication().AddFacebook("Facebook", options =>
            {
                options.AppId = facebookAppId;
                options.AppSecret = facebookAppSecret;
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.Fields.Add("name");
                options.Fields.Add("email");
            });
        }
    }

    private static RateLimitPartition<string> CreateAuthenticationPartition(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}:{context.Request.Path.Value?.ToLowerInvariant()}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = AuthenticationPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });

    private static void ConfigureIdentity(IdentityOptions options)
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequiredUniqueChars = 4;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    }

    private static void ConfigureServerCertificates(
        OpenIddictServerBuilder options,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            options.AddDevelopmentEncryptionCertificate();
            options.AddDevelopmentSigningCertificate();
            return;
        }

        options.AddEncryptionCertificate(LoadCertificate(configuration, "Encryption"));
        options.AddSigningCertificate(LoadCertificate(configuration, "Signing"));
    }

    private static X509Certificate2 LoadCertificate(IConfiguration configuration, string purpose)
    {
        var path = configuration[$"Authentication:{purpose}CertificatePath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                $"Authentication:{purpose}CertificatePath must be configured outside Development.");
        }

        return X509CertificateLoader.LoadPkcs12FromFile(
            path,
            configuration[$"Authentication:{purpose}CertificatePassword"],
            X509KeyStorageFlags.EphemeralKeySet);
    }
}
