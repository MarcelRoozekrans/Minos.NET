using System.Globalization;
using System.Net;
using Minos.Benchmarks.Mock;

namespace Minos.Benchmarks.Tests;

/// <summary>The Kestrel mock: the recorded bytes, 404 elsewhere, and the served-request count.</summary>
public sealed class MockHostTests : IAsyncLifetime, IDisposable
{
    private static readonly string ResponsePath = Path.Combine(AppContext.BaseDirectory, "response.json");

    private readonly HttpClient _http = new();
    private MockServer _server = null!;

    private Uri Url(string path) => new(_server.BaseAddress, path);

    public async Task InitializeAsync() => _server = await MockHost.StartAsync(0, ResponsePath, CancellationToken.None);

    [Fact]
    public async Task A_second_host_on_a_taken_port_fails_and_leaves_the_port_usable()
    {
        var port = _server.BaseAddress.Port;

        await Assert.ThrowsAnyAsync<IOException>(() => MockHost.StartAsync(port, ResponsePath, CancellationToken.None));

        // The first host still serves on the port.
        using (await PostSystemOneAsync())
        {
        }

        Assert.Equal(1, await CountAsync());

        // Once it stops, a new host can take the port.
        await _server.DisposeAsync();
        _server = await MockHost.StartAsync(port, ResponsePath, CancellationToken.None);
        Assert.Equal(port, _server.BaseAddress.Port);
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public void The_server_listens_on_the_loopback_address()
    {
        Assert.Equal("127.0.0.1", _server.BaseAddress.Host);
        Assert.NotEqual(0, _server.BaseAddress.Port);
    }

    [Fact]
    public async Task Post_to_systemone_returns_the_recorded_bytes_as_json()
    {
        using var response = await PostSystemOneAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.ToString());
        Assert.Equal(await File.ReadAllBytesAsync(ResponsePath), await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("GET", "/v1/other")]
    [InlineData("POST", "/v1/other")]
    [InlineData("POST", "/v1/systemone/extra")]
    [InlineData("GET", "/")]
    public async Task Another_path_answers_404(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), Url(path));
        using var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task Another_method_on_systemone_is_refused_and_not_counted()
    {
        using var response = await _http.GetAsync(Url(MockHost.SystemOnePath));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task Count_is_zero_then_one_then_reset_to_zero()
    {
        Assert.Equal(0, await CountAsync());

        using (await PostSystemOneAsync())
        {
        }

        Assert.Equal(1, await CountAsync());
        Assert.Equal(1, _server.Count);

        using var reset = await _http.PostAsync(Url(MockHost.CountResetPath), content: null);

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(0, await CountAsync());
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
    public async Task Count_and_reset_requests_are_not_counted()
    {
        for (var i = 0; i < 3; i++)
        {
            _ = await CountAsync();
            using var reset = await _http.PostAsync(Url(MockHost.CountResetPath), content: null);
        }

        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task A_thousand_calls_count_a_thousand()
    {
        const int Calls = 1000;
        for (var i = 0; i < Calls; i++)
        {
            using var response = await PostSystemOneAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(Calls, await CountAsync());
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    public void Dispose() => _http.Dispose();

    private async Task<HttpResponseMessage> PostSystemOneAsync()
    {
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        return await _http.PostAsync(Url(MockHost.SystemOnePath), content);
    }

    private async Task<long> CountAsync()
        => long.Parse(await _http.GetStringAsync(Url(MockHost.CountPath)), CultureInfo.InvariantCulture);
}
