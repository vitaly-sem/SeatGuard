namespace SeatGuard.Application.Abstractions.Messaging;

/// <summary>
/// Marks a command that must run inside a database transaction. A MediatR
/// behavior opens the transaction and commits it after the handler succeeds.
/// </summary>
public interface ITransactionalRequest;
