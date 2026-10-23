using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace SeatGuard.IntegrationTests.Infrastructure;

internal static class TestApi
{
    public const string AdminKey = "test-admin-key";

    public static async Task<Guid> CreateEventAsync(
        this HttpClient client,
        DateTimeOffset startsAt,
        int rows = 1,
        int seatsPerRow = 2,
        decimal price = 49m,
        string name = "Integration Event")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/events")
        {
            Content = JsonContent.Create(new
            {
                name,
                venue = "Test Hall",
                startsAt,
                sections = new[]
                {
                    new
                    {
                        name = "Orchestra",
                        rows,
                        seatsPerRow,
                        priceAmount = price,
                        currency = "EUR",
                    },
                },
            }),
        };

        request.Headers.Add("X-Api-Key", AdminKey);

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("eventId").GetGuid();
    }

    public static async Task<JsonElement> GetAvailabilityAsync(this HttpClient client, Guid eventId)
    {
        var response = await client.GetAsync($"/api/v1/events/{eventId}/availability");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<(HttpResponseMessage Response, JsonElement Body)> ReserveAsync(
        this HttpClient client,
        Guid eventId,
        Guid seatId,
        string customerId,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reservations")
        {
            Content = JsonContent.Create(new { eventId, seatId, customerId }),
        };

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return (response, body);
    }

    public static Guid SeatId(this JsonElement availability, string label)
        => availability
            .GetProperty("seats")
            .EnumerateArray()
            .Single(seat => seat.GetProperty("label").GetString() == label)
            .GetProperty("id")
            .GetGuid();

    public static string Status(this JsonElement seat) => seat.GetProperty("status").GetString()!;

    public static string ProblemCode(this JsonElement problem) => problem.GetProperty("code").GetString()!;

    public static string SeatStatus(this JsonElement availability, string label)
        => availability
            .GetProperty("seats")
            .EnumerateArray()
            .Single(seat => seat.GetProperty("label").GetString() == label)
            .Status();

    public static bool HasError(this JsonElement problem, string field)
        => problem.TryGetProperty("errors", out var errors)
            && errors.EnumerateObject().Any(property =>
                string.Equals(property.Name, field, StringComparison.OrdinalIgnoreCase));
}
