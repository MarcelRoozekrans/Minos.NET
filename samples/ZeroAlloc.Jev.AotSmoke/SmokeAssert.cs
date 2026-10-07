namespace ZeroAlloc.Jev.AotSmoke;

/// <summary>Helpers for checks that expect a throw, or that need an environment variable for one check only.</summary>
internal static class SmokeAssert
{
    /// <summary>Whether <paramref name="action"/> throws <typeparamref name="TException"/>.</summary>
    public static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    /// <summary>Whether <paramref name="action"/> throws <typeparamref name="TException"/>, synchronously or when awaited.</summary>
    public static async Task<bool> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    /// <summary>
    /// Sets the TypeSafe key and base address environment variables until disposed, then restores them, so a check
    /// can run the entry points that read them without leaking them into the next check.
    /// </summary>
    public static EnvironmentScope TypeSafeEnvironment()
        => new(
            (JevDefaults.ApiKeyEnvironmentVariable, "smoke-key"),
            (JevDefaults.BaseAddressEnvironmentVariable, "https://example.test/api/"));

    /// <summary>Removes the TypeSafe key environment variable until disposed, then restores it.</summary>
    public static EnvironmentScope NoApiKeyEnvironment() => new((JevDefaults.ApiKeyEnvironmentVariable, null));

    internal sealed class EnvironmentScope : IDisposable
    {
        private readonly (string Name, string? Value)[] previous;

        public EnvironmentScope(params (string Name, string? Value)[] variables)
        {
            previous = new (string Name, string? Value)[variables.Length];
            for (var i = 0; i < variables.Length; i++)
            {
                previous[i] = (variables[i].Name, Environment.GetEnvironmentVariable(variables[i].Name));
                Environment.SetEnvironmentVariable(variables[i].Name, variables[i].Value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
