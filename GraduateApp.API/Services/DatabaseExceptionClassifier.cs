using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Services;

internal static class DatabaseExceptionClassifier
{
    public static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException
                && sqlException.Errors.Cast<SqlError>().Any(error => error.Number is 2601 or 2627))
            {
                return true;
            }
        }

        return false;
    }
}
