namespace PharmaERP.Application.Common.Exceptions;

public class InsufficientStockException : ValidationException
{
    public InsufficientStockException(string message) : base(message)
    {
    }
}

