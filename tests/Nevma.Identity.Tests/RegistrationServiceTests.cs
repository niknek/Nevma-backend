using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Nevma.Contracts.Identity;
using Nevma.Identity.Api.Application.Authentication;
using Nevma.Identity.Api.Infrastructure.Authentication;
using Nevma.Identity.Api.Infrastructure.Persistence;

namespace Nevma.Identity.Tests;

public sealed class RegistrationServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("1234567890123456")]
    public async Task Invalid_phone_does_not_create_account_profile_or_notice(string? phone)
    {
        await using var testServices = await CreateServicesAsync();
        await using var scope = testServices.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        var result = await service.RegisterAsync(new RegisterUserRequest(
            "missing-phone@example.com", "Strong!Password123", "Test", null, phone));
        Assert.False(result.IsSuccess);
        Assert.Contains("Phone", result.Errors.Keys);
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Equal(0, await db.Profiles.CountAsync());
        Assert.Equal(0, await db.ContactJoinedNotices.CountAsync());
    }

    [Fact]
    public async Task Registration_links_matching_contacts_and_queues_one_notice_per_owner()
    {
        await using var testServices = await CreateServicesAsync();
        await using var scope = testServices.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.AddressBookContacts.AddRange(
            Nevma.Identity.Api.Domain.AddressBook.AddressBookContact.Create(owner, "Friend", "+306912345678", null, null, null, null, null, now),
            Nevma.Identity.Api.Domain.AddressBook.AddressBookContact.Create(owner, "Email friend", "6900000000", "friend@example.com", null, null, null, null, now),
            Nevma.Identity.Api.Domain.AddressBook.AddressBookContact.Create(otherOwner, "Friend", "00306912345678", null, null, null, null, null, now),
            Nevma.Identity.Api.Domain.AddressBook.AddressBookContact.Create(otherOwner, "Unrelated", "6999999999", null, null, null, null, null, now));
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        var request = new RegisterUserRequest("friend@example.com", "Strong!Password123", "Friend", null, "6912345678");
        var result = await service.RegisterAsync(request);
        Assert.True(result.IsSuccess);
        Assert.Equal(3, await db.AddressBookContacts.CountAsync(contact => contact.LinkedUserId == result.User!.Id));
        Assert.Equal(2, await db.ContactJoinedNotices.CountAsync());
        Assert.All(await db.ContactJoinedNotices.ToListAsync(), notice => Assert.Null(notice.DeliveredAt));
        Assert.False((await service.RegisterAsync(request)).IsSuccess);
        Assert.Equal(2, await db.ContactJoinedNotices.CountAsync());
    }

    [Fact]
    public async Task Registration_creates_one_account_and_profile()
    {
        await using var testServices = await CreateServicesAsync();
        await using var scope = testServices.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRegistrationService>();

        var result = await service.RegisterAsync(new RegisterUserRequest(
            "nikos@example.com",
            "Strong!Password123",
            "Nikos",
            null, "6912345678"));

        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(result.IsSuccess);
        Assert.Equal(1, await dbContext.Users.CountAsync());
        Assert.Equal(1, await dbContext.Profiles.CountAsync());
        Assert.Equal(result.User!.Id, await dbContext.Users.Select(user => user.Id).SingleAsync());
        var passwordHash = await dbContext.Users.Select(user => user.PasswordHash).SingleAsync();
        Assert.DoesNotContain("Strong!Password123", passwordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_email_does_not_create_a_second_profile()
    {
        await using var testServices = await CreateServicesAsync();
        await using var scope = testServices.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        var request = new RegisterUserRequest(
            "nikos@example.com",
            "Strong!Password123",
            "Nikos",
            null, "6912345678");

        var first = await service.RegisterAsync(request);
        var duplicate = await service.RegisterAsync(request with { DisplayName = "Other" });

        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(first.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(1, await dbContext.Profiles.CountAsync());
    }

    private static async Task<TestServices> CreateServicesAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddLogging();
        services.AddDbContext<IdentityDbContext>(options => options.UseSqlite(connection));
        services.AddNevmaAuthentication(configuration, new TestWebHostEnvironment());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IRegistrationService, RegistrationService>();

        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<IdentityDbContext>()
                .Database
                .EnsureCreatedAsync();
        }

        return new TestServices(provider, connection);
    }

    private sealed record TestServices(ServiceProvider Provider, SqliteConnection Connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Nevma.Identity.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
