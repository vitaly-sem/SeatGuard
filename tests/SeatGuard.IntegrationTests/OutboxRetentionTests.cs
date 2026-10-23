using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SeatGuard.Application.Abstractions.Messaging;
using SeatGuard.Infrastructure.Persistence;
using SeatGuard.IntegrationTests.Infrastructure;

namespace SeatGuard.IntegrationTests;

[Collection(IntegrationSuite.Name)]
public sealed class OutboxRetentionTests
{
    private readonly SeatGuardFixture _fixture;

    public OutboxRetentionTests(SeatGuardFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Cleaner_RemovesOnlyProcessedMessagesOlderThanRetention()
    {
        await using var app = await _fixture.StartAppAsync();
        var client = app.Client;

        _ = await client.CreateEventAsync(
            _fixture.Clock.GetUtcNow().AddDays(30),
            rows: 1,
            seatsPerRow: 1);

        await using (var scope = app.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
            (await dispatcher.DispatchOnceAsync()).Should().BeGreaterThan(0);
        }

        int processedBefore;

        await using (var scope = app.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SeatGuardDbContext>();
            processedBefore = await db.OutboxMessages.CountAsync(row => row.ProcessedAt != null);
        }

        processedBefore.Should().BeGreaterThan(0);

        _fixture.Clock.Advance(TimeSpan.FromDays(8));

        // A pending row must survive retention. Outbox dispatch is disabled in
        // the test host, so this message stays pending.
        _ = await client.CreateEventAsync(
            _fixture.Clock.GetUtcNow().AddDays(30),
            rows: 1,
            seatsPerRow: 1);

        int purged;

        await using (var scope = app.CreateScope())
        {
            var cleaner = scope.ServiceProvider.GetRequiredService<IOutboxCleaner>();
            purged = await cleaner.CleanupAsync();
        }

        purged.Should().Be(processedBefore);

        await using (var scope = app.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SeatGuardDbContext>();
            var rows = await db.OutboxMessages.AsNoTracking().ToListAsync();

            rows.Should().HaveCount(1);
            rows.Should().OnlyContain(row => row.ProcessedAt == null && row.FailedAt == null);

            var indexes = await db.Database
                .SqlQueryRaw<string>(
                    "SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'outbox_messages'")
                .ToListAsync();

            indexes.Should().Contain("ix_outbox_processed_at");
        }
    }
}
