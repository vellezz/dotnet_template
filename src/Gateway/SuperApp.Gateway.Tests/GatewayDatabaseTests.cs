using SuperApp.Gateway.Persistence.Seed;
using SuperApp.Gateway.Bff.DataProtection;
using SuperApp.Gateway.Bff.Sessions;
using SuperApp.Gateway.Hosting;
using SuperApp.Gateway.Proxy.Configuration;
using SuperApp.Gateway.Security;
using SuperApp.Gateway.Bff.Tokens;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using SuperApp.Gateway.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Testcontainers.MsSql;

namespace SuperApp.Gateway.Tests;

/// <summary>
/// The gateway schema on a real MSSQL (Testcontainers): migration, route configuration from the database (ADR-0022), BFF sessions and the
/// cross-replica token refresh (ADR-0011, ADR-0013).
/// </summary>
public sealed class GatewayDatabaseTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private readonly CapturingLoggerProvider _logs = new();
    private ServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:Profile"] = GatewayProfiles.Mobile,
                ["Gateway:ConfigPollInterval"] = "00:00:00.200",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(_logs));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<GatewayDbContext>(options => GatewayDbContextOptions.Configure(options, _container.GetConnectionString()));
        services.AddHybridCache();
        services.AddReverseProxy();
        GatewayPolicies.Register(services.AddAuthorizationBuilder());
        services.AddRateLimiter(options => GatewayRateLimits.Register(options, configuration));
        services.AddSingleton<DatabaseProxyConfigProvider>();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<DbTicketStore>();
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<GatewayDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task Seeded_routes_are_loaded_validated_and_invalid_changes_are_rejected()
    {
        var provider = _services.GetRequiredService<DatabaseProxyConfigProvider>();
        await provider.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => provider.IsLoaded);

        var config = provider.GetConfig();
        Assert.Equal(ProxyConfigurationSeed.Experiences.Select(experience => $"{experience.Experience}-bff").Order(), config.Routes.Select(route => route.RouteId).Order());
        Assert.All(config.Routes, route => Assert.DoesNotContain("internal", route.Match.Path!, StringComparison.OrdinalIgnoreCase));
        Assert.All(config.Clusters, cluster => Assert.EndsWith(".svc.cluster.local:8080", Assert.Single(cluster.Destinations!).Value.Address, StringComparison.Ordinal));
        var loadedVersion = provider.ActiveMigrationId;

        // A new "migration" with a route referencing a non-existent policy: the YARP validator rejects it and the working configuration stays.
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO [gateway].[Routes] ([Profile], [RouteId], [ClusterId], [Order], [Path], [AuthorizationPolicy])
                VALUES ('gateway-mobile', 'broken', 'example', 1, '/api/broken/{{**rest}}', 'does-not-exist');
                INSERT INTO [gateway].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES ('99999999999999_Broken', '10.0.0');
                """,
                TestContext.Current.CancellationToken);
        }

        // Several polls (200 ms each) see the same rejected migration: it is logged and remembered once, not re-read on every poll.
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);
        Assert.Equal(loadedVersion, provider.ActiveMigrationId);
        Assert.Equal("99999999999999_Broken", provider.RejectedMigrationId);
        Assert.DoesNotContain(provider.GetConfig().Routes, route => route.RouteId == "broken");
        Assert.Equal(1, _logs.Count(3002));
        Assert.Equal(0, _logs.Count(3003));

        // The fix is a newer migration: it is loaded and the rejected version is forgotten.
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM [gateway].[Routes] WHERE [Profile] = 'gateway-mobile' AND [RouteId] = 'broken';
                INSERT INTO [gateway].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES ('99999999999999_Fixed', '10.0.0');
                """,
                TestContext.Current.CancellationToken);
        }

        await WaitUntilAsync(() => provider.ActiveMigrationId == "99999999999999_Fixed");
        Assert.Null(provider.RejectedMigrationId);
        Assert.Equal(1, _logs.Count(3002));

        await provider.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Unsupported_transform_kind_is_reported_once_as_invalid_configuration()
    {
        var provider = _services.GetRequiredService<DatabaseProxyConfigProvider>();
        await provider.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => provider.IsLoaded);
        var loadedVersion = provider.ActiveMigrationId;

        // Simulates a row that bypassed the database constraints (for example a disabled CHECK): the mapper must reject it.
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE [gateway].[RouteTransforms] NOCHECK CONSTRAINT [CK_RouteTransforms_Kind], [CK_RouteTransforms_Fields];
                INSERT INTO [gateway].[RouteTransforms] ([Profile], [RouteId], [Order], [Kind], [Name], [Value])
                VALUES ('gateway-mobile', 'example-bff', 5, 'QueryValueSet', 'a', 'b');
                INSERT INTO [gateway].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES ('99999999999999_BadTransform', '10.0.0');
                """,
                TestContext.Current.CancellationToken);
        }

        await WaitUntilAsync(() => provider.RejectedMigrationId == "99999999999999_BadTransform");
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(loadedVersion, provider.ActiveMigrationId);
        Assert.Equal(1, _logs.Count(3002));
        Assert.Equal(0, _logs.Count(3003));

        await provider.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Database_accepts_only_in_cluster_service_addresses()
    {
        string[] rejected =
        [
            "https://evil.example.com",
            "http://evil.com/x.svc.cluster.local",
            "http://a.svc.cluster.local.evil.com",
            "http://a.b.svc.cluster.local.evil.com:80",
            "https://knowledge-api.knowledge.svc.cluster.local:8080",
            "http://knowledge-api.knowledge.svc.cluster.local",
            "http://knowledge-api.svc.cluster.local:8080",
            "http://user@knowledge-api.knowledge.svc.cluster.local:8080",
            "http://knowledge-api.knowledge.svc.cluster.local:8080/path",
            "http://knowledge-api.knowledge.svc.cluster.local:8080/?q=1",
            "http://knowledge-api.knowledge.svc.cluster.local:8080#f",
            "http://Knowledge-api.knowledge.svc.cluster.local:8080",
            "http://knowledge-.knowledge.svc.cluster.local:8080",
            "http://knowledge-api.knowledge.svc.cluster.local:0",
            "http://knowledge-api.knowledge.svc.cluster.local:65536",
            "http://knowledge-api.knowledge.svc.cluster.local:80x",
        ];

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        var accepted = new List<string>();
        foreach (var address in rejected)
        {
            try
            {
                await InsertDestinationAsync(db, "evil", address);
                accepted.Add(address);
            }
            catch (Microsoft.Data.SqlClient.SqlException exception)
            {
                Assert.Contains("CK_Destinations_ClusterAddress", exception.Message, StringComparison.Ordinal);
            }
        }

        Assert.Empty(accepted);

        await InsertDestinationAsync(db, "ok-1", "http://knowledge-api.knowledge.svc.cluster.local:8080/");
        await InsertDestinationAsync(db, "ok-2", "http://a1.b-2.svc.cluster.local:65535");
    }

    [Fact]
    public async Task Token_refresh_runs_once_per_session_across_replicas_and_is_never_undone()
    {
        var tokenEndpoint = new RotatingTokenEndpoint();
        var (storeA, refresherA) = CreateReplica(tokenEndpoint);
        var (storeB, refresherB) = CreateReplica(tokenEndpoint);
        var key = await storeA.StoreAsync(Ticket(expiresIn: TimeSpan.FromSeconds(10)));
        var staleTicket = await storeB.RetrieveAsync(key);

        var contextA = await ValidationContextAsync(storeA, key);
        var contextB = await ValidationContextAsync(storeB, key);
        await Task.WhenAll(refresherA.ValidatePrincipalAsync(contextA), refresherB.ValidatePrincipalAsync(contextB));

        Assert.Equal(1, tokenEndpoint.Calls);
        foreach (var context in new[] { contextA, contextB })
        {
            Assert.NotNull(context.Principal);
            Assert.Equal("access-1", context.Properties.GetTokenValue("access_token"));
            Assert.Equal("refresh-1", context.Properties.GetTokenValue("refresh_token"));
        }

        // A request that loaded the ticket before the refresh renews it afterwards (sliding expiration): the rotated tokens stay.
        await storeB.RenewAsync(key, staleTicket!);
        var stored = await storeA.RetrieveAsync(key);
        Assert.Equal("access-1", stored!.Properties.GetTokenValue("access_token"));
        Assert.Equal("refresh-1", stored.Properties.GetTokenValue("refresh_token"));
    }

    [Fact]
    public async Task Aborting_the_request_that_started_a_refresh_does_not_fail_the_others()
    {
        var tokenEndpoint = new RotatingTokenEndpoint();
        var (store, refresher) = CreateReplica(tokenEndpoint);
        var key = await store.StoreAsync(Ticket(expiresIn: TimeSpan.FromSeconds(10)));

        using var aborted = new CancellationTokenSource();
        var first = await ValidationContextAsync(store, key, aborted);
        var second = await ValidationContextAsync(store, key);
        var firstRefresh = refresher.ValidatePrincipalAsync(first);
        var secondRefresh = refresher.ValidatePrincipalAsync(second);
        await aborted.CancelAsync();
        await Task.WhenAll(firstRefresh, secondRefresh);

        Assert.Equal(1, tokenEndpoint.Calls);
        Assert.Equal("access-1", first.Properties.GetTokenValue("access_token"));
        Assert.Equal("access-1", second.Properties.GetTokenValue("access_token"));
    }

    [Fact]
    public async Task Session_ticket_is_stored_encrypted_and_revoked_by_backchannel_logout()
    {
        var store = _services.GetRequiredService<DbTicketStore>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1"), new Claim("sid", "session-1")], "test"));
        var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) };
        properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = "secret-token" }]);

        var key = await store.StoreAsync(new AuthenticationTicket(principal, properties, "Cookies"));
        var restored = await store.RetrieveAsync(key);

        Assert.Equal("secret-token", restored!.Properties.GetTokenValue("access_token"));
        await using (var scope = _services.CreateAsyncScope())
        {
            var raw = await scope.ServiceProvider.GetRequiredService<GatewayDbContext>().Sessions.SingleAsync(session => session.Id == key, TestContext.Current.CancellationToken);
            Assert.DoesNotContain("secret-token", System.Text.Encoding.UTF8.GetString(raw.Value), StringComparison.Ordinal);
        }

        Assert.Equal(1, await store.RemoveBySessionAsync("session-1", null, TestContext.Current.CancellationToken));
        Assert.Null(await store.RetrieveAsync(key));
    }

    [Fact]
    public async Task Data_protection_keys_are_encrypted_with_certificate()
    {
        var certificatePath = Path.Combine(Path.GetTempPath(), $"dp-{Guid.NewGuid():N}.pfx");
        using (var rsa = System.Security.Cryptography.RSA.Create(2048))
        {
            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                "CN=superapp-gateway-data-protection", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            await File.WriteAllBytesAsync(certificatePath, certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, "test"), TestContext.Current.CancellationToken);
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:CertificatePath"] = certificatePath,
                ["DataProtection:CertificatePassword"] = "test",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<GatewayDbContext>(options => GatewayDbContextOptions.Configure(options, _container.GetConnectionString()));
        services.AddGatewayDataProtection(configuration, new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" });
        await using var provider = services.BuildServiceProvider();

        var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
        Assert.Equal("sekret", protector.Unprotect(protector.Protect("sekret")));

        await using var scope = provider.CreateAsyncScope();
        var keys = await scope.ServiceProvider.GetRequiredService<GatewayDbContext>().DataProtectionKeys.ToListAsync(TestContext.Current.CancellationToken);
        var key = Assert.Single(keys);
        Assert.Contains("encryptedSecret", key.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<value>", key.Xml, StringComparison.Ordinal);
        File.Delete(certificatePath);
    }

    [Fact]
    public void Data_protection_requires_certificate_outside_development()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddGatewayDataProtection(
            new ConfigurationBuilder().Build(),
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" }));

        Assert.Contains("ADR-0013", exception.Message, StringComparison.Ordinal);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    private static Task<int> InsertDestinationAsync(GatewayDbContext db, string destinationId, string address) =>
        db.Database.ExecuteSqlAsync(
            $"INSERT INTO [gateway].[Destinations] ([Profile], [ClusterId], [DestinationId], [Address]) VALUES ('bff-web', 'example', {destinationId}, {address})",
            TestContext.Current.CancellationToken);

    // One gateway replica: its own session store and token refresher (own single-flight map) over the shared database and key ring.
    private (DbTicketStore Store, TokenRefresher Refresher) CreateReplica(RotatingTokenEndpoint tokenEndpoint)
    {
        var store = new DbTicketStore(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<IDataProtectionProvider>(),
            TimeProvider.System);
        var oidc = new OpenIdConnectOptions
        {
            ClientId = "bff-web",
            ClientSecret = "secret",
            ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration { TokenEndpoint = "https://ciam.test/token" }),
        };
        var refresher = new TokenRefresher(
            new SingleClientFactory(tokenEndpoint),
            new StaticOptionsMonitor<OpenIdConnectOptions>(oidc),
            store,
            TimeProvider.System,
            NullLogger<TokenRefresher>.Instance);
        return (store, refresher);
    }

    private static AuthenticationTicket Ticket(TimeSpan expiresIn)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1"), new Claim("sid", "session-1")], "Cookies"));
        var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) };
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = "access-0" },
            new AuthenticationToken { Name = "refresh_token", Value = "refresh-0" },
            new AuthenticationToken { Name = "expires_at", Value = DateTimeOffset.UtcNow.Add(expiresIn).ToString("o", CultureInfo.InvariantCulture) },
        ]);
        return new AuthenticationTicket(principal, properties, "Cookies");
    }

    private static async Task<CookieValidatePrincipalContext> ValidationContextAsync(DbTicketStore store, string key, CancellationTokenSource? requestAborted = null)
    {
        var ticket = await store.RetrieveAsync(key);
        return new CookieValidatePrincipalContext(
            new DefaultHttpContext { RequestAborted = requestAborted?.Token ?? CancellationToken.None },
            new AuthenticationScheme("Cookies", null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            ticket!);
    }

    // CIAM token endpoint with refresh token rotation: every refresh token can be used once; a second use is rejected (invalid_grant).
    private sealed class RotatingTokenEndpoint : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, bool> _used = new();
        private int _calls;

        public int Calls => _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            var refreshToken = form.Split('&').Select(Uri.UnescapeDataString).Single(pair => pair.StartsWith("refresh_token=", StringComparison.Ordinal));
            await Task.Delay(300, cancellationToken);

            if (!_used.TryAdd(refreshToken, true))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"invalid_grant"}""") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"access_token":"access-{{call}}","refresh_token":"refresh-{{call}}","expires_in":300}"""),
            };
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    // Records the EventIds of all log entries, so tests can assert how often a message was written.
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<int> _eventIds = new();

        public int Count(int eventId) => _eventIds.Count(id => id == eventId);

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_eventIds);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<int> eventIds) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                eventIds.Enqueue(eventId.Id);
        }
    }
}
