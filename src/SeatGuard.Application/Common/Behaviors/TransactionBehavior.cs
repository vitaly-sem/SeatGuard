using MediatR;
using SeatGuard.Application.Abstractions.Messaging;
using SeatGuard.Application.Abstractions.Persistence;

namespace SeatGuard.Application.Common.Behaviors;

/// <summary>
/// Wraps commands marked with <see cref="ITransactionalRequest"/> in a database
/// transaction. Nested transactions are flattened by the unit of work, so handlers
/// can call other transactional commands safely.
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ITransactionalRequest)
        {
            return next(cancellationToken);
        }

        return unitOfWork.ExecuteInTransactionAsync(token => next(token), cancellationToken);
    }
}
