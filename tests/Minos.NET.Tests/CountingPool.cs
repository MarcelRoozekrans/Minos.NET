using System.Buffers;

namespace Minos.Tests;

/// <summary>An <see cref="ArrayPool{T}"/> over the shared pool that tracks which of its arrays are still rented.</summary>
internal sealed class CountingPool : ArrayPool<byte>
{
    private readonly Lock _gate = new();
    private readonly HashSet<byte[]> _outstanding = [];
    private int _rented;

    /// <summary>Gets the number of arrays rented so far.</summary>
    public int Rented
    {
        get
        {
            lock (_gate)
            {
                return _rented;
            }
        }
    }

    /// <summary>Gets the number of arrays rented and not yet returned.</summary>
    public int Outstanding
    {
        get
        {
            lock (_gate)
            {
                return _outstanding.Count;
            }
        }
    }

    public override byte[] Rent(int minimumLength)
    {
        var array = Shared.Rent(minimumLength);
        lock (_gate)
        {
            _outstanding.Add(array);
            _rented++;
        }

        return array;
    }

    public override void Return(byte[] array, bool clearArray = false)
    {
        lock (_gate)
        {
            if (!_outstanding.Remove(array))
            {
                throw new InvalidOperationException("The array was not rented from this pool, or was returned twice.");
            }
        }

        Shared.Return(array, clearArray);
    }
}
