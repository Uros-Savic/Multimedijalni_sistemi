namespace MSI.Core.MsiFormat;

public sealed class MsiFormatException : Exception
{
    public MsiFormatException(string message) : base(message) { }
    public MsiFormatException(string message, Exception inner) : base(message, inner) { }
}
