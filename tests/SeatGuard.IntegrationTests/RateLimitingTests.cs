using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SeatGuard.IntegrationTests.Infrastructure;

namespace SeatGuard.IntegrationTests;

[Collection(IntegrationSuite.Name)]
public sealed class RateLimitingTests
{
    private readonly SeatGuardFixture _fixture;

    public RateLimitingTests(SeatGuardFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task InvalidAdminKeys_AreThrottled_AndDoNotExhaustTheValidKeyBucket()
    {
        using var environment = new EnvironmentScope("RateLimiting__AdminPerMinute", "5");

        await using var app = await _fixture.StartAppAsync();
        var client = app.Client;

        var statuses = new List<HttpStatusCode>();

        for (var index = 0; index < 12; index++)
        {
            using var response = await SendCreateEventAsync(client, "wrong-admin-key");
            statuses.Add(response.StatusCode);
        }

        statuses.Take(5).Should().OnlyContain(status => status == HttpStatusCode.Unauthorized);
        statuses.Skip(5).Should().OnlyContain(status => status == HttpStatusCode.TooManyRequests);

        // The valid key has its own bucket, so a bad-key flood cannot lock the
        // administrator out.
        using var valid = await SendCreateEventAsync(client, TestApi.AdminKey);
        valid.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ForwardedClientAddresses_GetIndependentBuckets_WhenTheProxyIsTrusted()
    {
        using var environment = new EnvironmentScope(
            ("RateLimiting__BookingsPerMinute", "2"),
            ("ForwardedHeaders__Enabled", "true"),
            ("ForwardedHeaders__KnownProxies__0", "10.0.0.1"));

        await using var app = await _fixture.StartAppAsync(configureServices: services =>
            services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(IPAddress.Parse("10.0.0.1"))));

        var client = app.Client;
        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 1);
        var seatId = (await client.GetAvailabilityAsync(eventId)).SeatId("Orchestra · A1");

        var first = await ReserveAsync(client, eventId, seatId, "customer-a", "203.0.113.10", "racer-a");
        var second = await ReserveAsync(client, eventId, seatId, "customer-b", "203.0.113.10", "racer-b");
        var third = await ReserveAsync(client, eventId, seatId, "customer-c", "203.0.113.10", "racer-c");

        first.Should().Be(HttpStatusCode.Created);
        second.Should().Be(HttpStatusCode.Conflict);
        third.Should().Be(HttpStatusCode.TooManyRequests);

        // A different forwarded client address starts with a fresh budget.
        var otherClient = await ReserveAsync(client, eventId, seatId, "customer-d", "203.0.113.11", "racer-d");
        otherClient.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ForwardedHeader_IsIgnored_FromAnUntrustedProxy()
    {
        using var environment = new EnvironmentScope(
            ("RateLimiting__BookingsPerMinute", "2"),
            ("ForwardedHeaders__Enabled", "true"),
            ("ForwardedHeaders__KnownProxies__0", "10.0.0.1"));

        await using var app = await _fixture.StartAppAsync(configureServices: services =>
            services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(IPAddress.Parse("10.0.0.9"))));

        var client = app.Client;
        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 1);
        var seatId = (await client.GetAvailabilityAsync(eventId)).SeatId("Orchestra · A1");

        var first = await ReserveAsync(client, eventId, seatId, "customer-a", "203.0.113.10", "racer-a");
        var second = await ReserveAsync(client, eventId, seatId, "customer-b", "203.0.113.11", "racer-b");
        var third = await ReserveAsync(client, eventId, seatId, "customer-c", "not-an-ip", "racer-c");

        first.Should().Be(HttpStatusCode.Created);
        second.Should().Be(HttpStatusCode.Conflict);
        third.Should().Be(HttpStatusCode.TooManyRequests, "the untrusted address is shared by every forwarded value");
    }

    [Fact]
    public async Task ReadEndpoints_AreThrottled()
    {
        using var environment = new EnvironmentScope("RateLimiting__ReadsPerMinute", "3");

        await using var app = await _fixture.StartAppAsync();
        var client = app.Client;

        var eventId = await client.CreateEventAsync(_fixture.Clock.GetUtcNow().AddDays(30), rows: 1, seatsPerRow: 1);

        for (var index = 0; index < 3; index++)
        {
            (await client.GetAsync($"/api/v1/events/{eventId}/availability"))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await client.GetAsync($"/api/v1/events/{eventId}/availability"))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private async Task<HttpResponseMessage> SendCreateEventAsync(HttpClient client, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/events")
        {
            Content = JsonContent.Create(new
            {
                name = "Rate limit event",
                venue = "Test Hall",
                startsAt = _fixture.Clock.GetUtcNow().AddDays(30),
                sections = new[]
                {
                    new
                    {
                        name = "Orchestra",
                        rows = 1,
                        seatsPerRow = 1,
                        priceAmount = 10m,
                        currency = "EUR",
                    },
                },
            }),
        };

        request.Headers.Add("X-Api-Key", apiKey);

        return await client.SendAsync(request);
    }

    private static async Task<HttpStatusCode> ReserveAsync(
        HttpClient client,
        Guid eventId,
        Guid seatId,
        string customerId,
        string forwardedFor,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reservations")
        {
            Content = JsonContent.Create(new { eventId, seatId, customerId }),
        };

        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>
    /// TestServer has no socket, so the remote address is injected to exercise
    /// the trusted-proxy checks of the forwarded-headers middleware.
    /// </summary>
    private sealed class RemoteIpStartupFilter(IPAddress remoteAddress) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = remoteAddress;
                return nextMiddleware(context);
            });

            next(app);
        };
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = [];

        public EnvironmentScope(string key, string value)
            : this(new KeyValuePair<string, string>(key, value))
        {
        }

        public EnvironmentScope(params (string Key, string Value)[] values)
            : this(values.Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value)).ToArray())
        {
        }

        public EnvironmentScope(params KeyValuePair<string, string>[] values)
        {
            foreach (var (key, value) in values)
            {
                _previous[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, value) in _previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
