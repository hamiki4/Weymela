using Microsoft.EntityFrameworkCore;
using System.Data;
using Npgsql;
using Weymela.Application;

namespace Weymela.Infrastructure.Persistence.Transactions;

public sealed class EfUnitOfWork(WeymelaDbContext db, IsolationLevel isolation = IsolationLevel.ReadCommitted) : IUnitOfWork
{
    public async Task CommitAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("Nested command transactions are not supported.");
        await using var transaction = await db.Database.BeginTransactionAsync(isolation, ct);
        try
        {
            var result = await operation(ct);
            await CommitAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear(); // never reuse mutated entities after a failed unit of work
            if (ex is DbUpdateConcurrencyException || IsSerializationConflict(ex))
                throw new ApplicationFailure(FailureKind.ConcurrencyConflict, "Concurrent financial change detected; reload and resubmit explicitly.");
            if (ex is PostgresException { SqlState: "23514" or "23503" })
                throw new ApplicationFailure(FailureKind.Validation, "The transaction violates a deferred financial invariant.", ex);
            if (ex is DbUpdateException { InnerException: PostgresException pg })
            {
                if (pg.SqlState is "40001" or "40P01")
                    throw new ApplicationFailure(FailureKind.ConcurrencyConflict, "Concurrent financial change detected; retry explicitly with the original idempotency key.");
                if (pg.SqlState == "23505")
                    throw new ApplicationFailure(pg.ConstraintName?.Contains("Idempotency") == true ? FailureKind.IdempotencyConflict : FailureKind.ConcurrencyConflict,
                        "A concurrent or duplicate operation conflicts with an existing record.");
                if (pg.SqlState is "23514" or "23503") throw new ApplicationFailure(FailureKind.Validation, "The operation violates a database financial invariant.", ex);
            }
            throw;
        }
    }

    // Npgsql's non-retrying execution strategy can wrap DbUpdateException in
    // InvalidOperationException. Preserve the concurrency classification through that wrapper.
    private static bool IsSerializationConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException { SqlState: "40001" or "40P01" }) return true;
        return false;
    }
}
