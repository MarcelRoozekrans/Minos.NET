using System.Buffers;

namespace Minos.OpenAI.Tests;

/// <summary>An <see cref="ArrayPool{T}"/> over the shared pool that counts the arrays still rented.</summary>
internal sealed class CountingPool : ArrayPool<byte>
{
    private int _outstanding;

    /// <summary>Gets the number of arrays rented and not yet returned.</summary>
    public int Outstanding => Volatile.Read(ref _outstanding);

    public override byte[] Rent(int minimumLength)
    {
        Interlocked.Increment(ref _outstanding);
        return Shared.Rent(minimumLength);
    }

    public override void Return(byte[] array, bool clearArray = false)
    {
        Interlocked.Decrement(ref _outstanding);
        Shared.Return(array, clearArray);
    }
}
