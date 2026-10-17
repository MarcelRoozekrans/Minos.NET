namespace Minos.Tool;

/// <summary>An input file, such as the dataset or the results, could not be read.</summary>
internal sealed class InputReadException : Exception
{
    public InputReadException()
    {
    }

    public InputReadException(string message)
        : base(message)
    {
    }

    public InputReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
