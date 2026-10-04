using System.Globalization;
using System.Net;
using ZeroAlloc.Jev.Benchmarks.Mock;

namespace ZeroAlloc.Jev.Benchmarks.Tests;

public sealed class MockHostTests : IDisposable
{
    private static readonly string ResponsePath = Path.Combine(AppContext.BaseDirectory, "response.json");

    private readonly WireMock.Server.WireMockServer _server = MockHost.Start(0, ResponsePath);
    private readonly HttpClient _http = new();

    private Uri Url(string path) => new(_server.Urls[0] + path);

    [Fact]
    public async Task Post_to_systemone_returns_the_recorded_bytes_as_json()
    {
        using var response = await PostSystemOneAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await File.ReadAllBytesAsync(ResponsePath), await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Another_path_gets_no_match()
    {
        using var response = await _http.GetAsync(Url("/v1/other"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Count_is_zero_before_any_call()
    {
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task Count_is_one_after_one_call()
    {
        using var response = await PostSystemOneAsync();

        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task Count_answers_as_plain_text()
    {
        using var response = await _http.GetAsync(Url(MockHost.CountPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("0", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reset_sets_the_count_back_to_zero()
    {
        using (await PostSystemOneAsync())
        using (await PostSystemOneAsync())
        {
        }

        using var reset = await _http.PostAsync(Url(MockHost.CountResetPath), content: null);

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task Count_and_reset_requests_are_not_counted()
    {
        _ = await CountAsync();
        using var reset = await _http.PostAsync(Url(MockHost.CountResetPath), content: null);
        using var miss = await _http.GetAsync(Url("/v1/other"));

        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task A_thousand_calls_count_a_thousand_and_keep_no_log()
    {
        const int Calls = 1000;
        for (var i = 0; i < Calls; i++)
        {
            using var response = await PostSystemOneAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(Calls, await CountAsync());
        Assert.Empty(_server.LogEntries);
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Stop();
        _server.Dispose();
    }

    private async Task<HttpResponseMessage> PostSystemOneAsync()
    {
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        return await _http.PostAsync(Url("/v1/systemone"), content);
    }

    private async Task<long> CountAsync()
        => long.Parse(await _http.GetStringAsync(Url(MockHost.CountPath)), CultureInfo.InvariantCulture);
}
