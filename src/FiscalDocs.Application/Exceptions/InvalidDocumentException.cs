namespace FiscalDocs.Application.Exceptions;

public sealed class InvalidDocumentException : Exception
{
    public InvalidDocumentException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}