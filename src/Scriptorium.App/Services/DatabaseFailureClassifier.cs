using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Scriptorium.App.Services;

internal static class DatabaseFailureClassifier
{
    public static bool IsDatabaseFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(exception);

        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            if (current is SqliteException or DbException or DbUpdateException)
            {
                return true;
            }

            if (current.InnerException is { } innerException)
            {
                pending.Push(innerException);
            }

            if (current is AggregateException aggregateException)
            {
                foreach (var aggregateInnerException in aggregateException.InnerExceptions)
                {
                    pending.Push(aggregateInnerException);
                }
            }
        }

        return false;
    }
}
