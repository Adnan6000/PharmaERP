using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PharmaERP.Application.Common.Exceptions;

namespace PharmaERP.Infrastructure.Persistence.Helpers;

public static class DbExceptionHelper
{
    public static Exception TranslateException(Exception ex)
    {
        if (ex is DbUpdateConcurrencyException)
        {
            return new ConcurrencyConflictException("This record was changed by another user. Refresh and try again.", ex);
        }

        if (ex is DbUpdateException dbUpdateEx && dbUpdateEx.InnerException is SqlException sqlEx)
        {
            // SQL Server Error 2601: Cannot insert duplicate key row in object with unique index
            // SQL Server Error 2627: Violation of %ls constraint '%.*ls'. Cannot insert duplicate key in object '%.*ls'
            if (sqlEx.Number is 2601 or 2627)
            {
                return new DuplicateKeyException("A record with the specified unique code, barcode, or name already exists.", sqlEx);
            }
        }

        return ex;
    }

    public static bool IsUniqueConstraintViolation(Exception ex)
    {
        if (ex is DbUpdateException dbUpdateEx && dbUpdateEx.InnerException is SqlException sqlEx)
        {
            return sqlEx.Number is 2601 or 2627;
        }

        if (ex is SqlException sqlDirectEx)
        {
            return sqlDirectEx.Number is 2601 or 2627;
        }

        return false;
    }
}

