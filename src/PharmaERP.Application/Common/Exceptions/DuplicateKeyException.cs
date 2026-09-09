namespace PharmaERP.Application.Common.Exceptions;

public class DuplicateKeyException : Exception
{
    public string? FieldName { get; }

    public DuplicateKeyException(string message, string? fieldName = null)
        : base(message)
    {
        FieldName = fieldName;
    }

    public DuplicateKeyException(string message, Exception innerException, string? fieldName = null)
        : base(message, innerException)
    {
        FieldName = fieldName;
    }
}

