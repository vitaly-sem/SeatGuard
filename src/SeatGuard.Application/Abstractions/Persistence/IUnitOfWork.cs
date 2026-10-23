namespace SeatGuard.Application.Abstractions.Persistence;

/// <summary>
/// Transactional boundary for command handlers. Infrastructure implements this
/// on top of the EF Core DbContext so the application layer never references EF.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}
