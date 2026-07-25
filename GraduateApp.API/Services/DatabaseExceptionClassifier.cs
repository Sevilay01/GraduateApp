using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Services;

internal static class DatabaseExceptionClassifier
{
    private static readonly HashSet<int> UnavailableErrorNumbers =
    [
        -2,
        2,
        20,
        53,
        64,
        121,
        233,
        258,
        4060,
        10053,
        10054,
        10060,
        11001,
        18456
    ];

    public static bool IsUniqueConstraintViolation(Exception exception) =>
        ContainsSqlError(exception, static number => number is 2601 or 2627);

    public static bool IsDeadlock(Exception exception) =>
        ContainsSqlError(exception, static number => number == 1205);

    public static bool IsUnavailable(Exception exception) =>
        ContainsSqlError(exception, UnavailableErrorNumbers.Contains);

    private static bool ContainsSqlError(Exception exception, Func<int, bool> predicate)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException
                && (predicate(sqlException.Number)
                    || sqlException.Errors.Cast<SqlError>().Any(error => predicate(error.Number))))
            {
                return true;
            }
        }

        return false;
    }
}
