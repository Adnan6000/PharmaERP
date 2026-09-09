namespace PharmaERP.Application.Common.Exceptions;

public class ValidationException : Exception
{
    public string? PropertyName { get; }

    public ValidationException(string message, string? propertyName = null)
        : base(message)
    {
        PropertyName = propertyName;
    }

    public ValidationException(string message, Exception innerException, string? propertyName = null)
        : base(message, innerException)
    {
        PropertyName = propertyName;
    }
}

