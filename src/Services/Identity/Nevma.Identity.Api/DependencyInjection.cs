using Microsoft.EntityFrameworkCore;
using Nevma.Identity.Api.Application.Authentication;
using Nevma.Identity.Api.Application;
using Nevma.Identity.Api.Application.Connections;
using Nevma.Identity.Api.Application.Users;
using Nevma.Identity.Api.Infrastructure.Authentication;
using Nevma.Identity.Api.Infrastructure.Connections;
using Nevma.Identity.Api.Infrastructure.Persistence;
using Nevma.Identity.Api.Infrastructure.Users;
using Nevma.Identity.Api.Application.Sessions;
using Nevma.Identity.Api.Infrastructure.Sessions;
using Nevma.Identity.Api.Application.Security;
using Nevma.Identity.Api.Infrastructure.Security;
using Nevma.Identity.Api.Application.AddressBook;
using Nevma.Identity.Api.Infrastructure.AddressBook;
using Nevma.ServiceDefaults.Extensions;

namespace Nevma.Identity.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityService(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddNevmaDataProtection(configuration, "Nevma.Identity");
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("IdentityDatabase"),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "identity");
                    npgsqlOptions.EnableRetryOnFailure();
                }));
        services.AddNevmaAuthentication(configuration, environment);
        services.AddScoped<IIdentityUnitOfWork>(provider => provider.GetRequiredService<IdentityDbContext>());
        services.AddScoped<IContactConnectionRepository, EfContactConnectionRepository>();
        services.AddScoped<ContactConnectionService>();
        services.AddScoped<AddressBookService>();
        services.AddScoped<IAddressBookRepository, EfAddressBookRepository>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IUserPrivacyRepository, EfUserPrivacyRepository>();
        services.AddScoped<UserService>();
        services.AddScoped<UserPrivacyService>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<IAccountRecoveryService, AccountRecoveryService>();
        services.AddScoped<IMultiFactorAuthenticationService, MultiFactorAuthenticationService>();
        var emailOptions = configuration
            .GetSection(IdentityEmailOptions.SectionName)
            .Get<IdentityEmailOptions>() ?? new IdentityEmailOptions();
        services.AddSingleton(emailOptions);
        services.AddSingleton<IIdentityEmailSender, SmtpIdentityEmailSender>();
        services.AddScoped<IDeviceSessionRepository, EfDeviceSessionRepository>();
        services.AddScoped<ISessionTokenRevoker, OpenIddictSessionTokenRevoker>();
        services.AddScoped<DeviceSessionService>();
        services.AddScoped<ISecurityEventRepository, EfSecurityEventRepository>();
        services.AddScoped<SecurityEventService>();
        services.AddHostedService<OpenIddictSeeder>();
        return services;
    }
}
