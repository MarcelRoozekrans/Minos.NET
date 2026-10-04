using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(Url("/v1/systemone"), content);

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
    public async Task Admin_request_count_is_one_after_one_call()
    {
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var post = await _http.PostAsync(Url("/v1/systemone"), content);

        var entries = await _http.GetFromJsonAsync<JsonElement>(Url("/__admin/requests"));

        Assert.Equal(1, entries.GetArrayLength());
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Stop();
        _server.Dispose();
    }
}
