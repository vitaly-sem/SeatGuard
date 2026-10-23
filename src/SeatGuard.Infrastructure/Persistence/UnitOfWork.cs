using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SeatGuard.Application.Abstractions.Persistence;
using SeatGuard.Application.Common.Exceptions;

namespace SeatGuard.Infrastructure.Persistence;

internal sealed class UnitOfWork(SeatGuardDbContext dbContext) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // The optimistic strategy lost the race for a seat row.
            throw new ConcurrencyConflictException(
                "The seat was modified by a concurrent request. Choose another seat.",
                exception);
        }
        catch (DbUpdateException exception) when (TryTranslateConstraint(exception, out var translated))
        {
            throw translated;
        }
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync(
            async token =>
            {
                await operation(token);
                return true;
            },
            cancellationToken);

    /// <summary>
    /// Turns database constraints into domain-level outcomes. The partial unique
    /// index is the final arbiter when two writers slip past the application-level
    /// strategy at the same time.
    /// </summary>
    private static bool TryTranslateConstraint(DbUpdateException exception, out Exception translated)
    {
        translated = exception;

        if (exception.InnerException is not PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
        {
            return false;
        }

        translated = postgres.ConstraintName switch
        {
            "ux_reservations_active_seat" => new ConcurrencyConflictException(
                "The seat is already reserved by another request.",
                exception),
            "ux_reservations_idempotency" => new DuplicateIdempotencyKeyException(),
            "ux_reservations_ticket_code" => new ConcurrencyConflictException(
                "A ticket code collision occurred. Retry the confirmation.",
                exception),
            _ => exception
        };

        return !ReferenceEquals(translated, exception);
    }
}
