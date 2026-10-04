namespace ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

/// <summary>An adapter's <see cref="HttpClient"/>, exposed so the tests can check it came from <see cref="BenchmarkTransport"/>.</summary>
internal interface IUsesBenchmarkTransport
{
    /// <summary>Gets the client the adapter's library sends through.</summary>
    HttpClient Http { get; }
}
