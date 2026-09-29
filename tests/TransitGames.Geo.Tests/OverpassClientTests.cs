using System.Net;
using System.Text;
using TransitGames.Geo.Overpass;

namespace TransitGames.Geo.Tests;

public class OverpassClientTests
{
    private const string Answer = """{"version":0.6,"elements":[{"type":"node","id":7,"lat":52.1,"lon":21.1,"tags":{"name":"X"}}]}""";

    private static readonly OverpassOptions Options = new()
    {
        Endpoints = [new("https://first.test/api"), new("https://second.test/api")],
        UserAgent = "TransitGames-Test/1.0",
        RetryDelay = TimeSpan.Zero,
    };

    [Fact]
    public async Task SendsQueryAsFormWithUserAgent()
    {
        var handler = new FakeHandler(_ => Json(Answer));
        var client = new OverpassClient(new HttpClient(handler), Options);

        var response = await client.QueryAsync("node(1);out;");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("first.test", request.Uri.Host);
        Assert.Equal("TransitGames-Test/1.0", request.UserAgent);
        Assert.Equal("data=node%281%29%3Bout%3B", request.Body);
        Assert.Equal(7, Assert.Single(response.Elements).Id);
        Assert.Equal("X", response.Elements[0].Tag("name"));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task BusyServer_FallsBackToNextEndpoint(HttpStatusCode status)
    {
        var handler = new FakeHandler(r => r.RequestUri!.Host == "first.test" ? new HttpResponseMessage(status) : Json(Answer));
        var client = new OverpassClient(new HttpClient(handler), Options);

        var response = await client.QueryAsync("q");

        Assert.Equal(["first.test", "second.test"], handler.Requests.Select(r => r.Uri.Host));
        Assert.Single(response.Elements);
    }

    [Fact]
    public async Task RuntimeErrorRemark_FallsBackToNextEndpoint()
    {
        var handler = new FakeHandler(r => r.RequestUri!.Host == "first.test"
            ? Json("""{"elements":[],"remark":"runtime error: Query timed out"}""")
            : Json(Answer));
        var client = new OverpassClient(new HttpClient(handler), Options);

        var response = await client.QueryAsync("q");

        Assert.Single(response.Elements);
    }

    [Fact]
    public async Task AllEndpointsFailing_Throws()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("offline"));
        var client = new OverpassClient(new HttpClient(handler), Options);

        var ex = await Assert.ThrowsAsync<OverpassException>(() => client.QueryAsync("q"));
        Assert.Contains("offline", ex.Message);
        Assert.Equal(4, handler.Requests.Count); // 2 endpoints × 2 rounds
    }

    [Fact]
    public async Task AllBusy_RetriesInSecondRound()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls <= 2 ? new HttpResponseMessage(HttpStatusCode.GatewayTimeout) : Json(Answer));
        var client = new OverpassClient(new HttpClient(handler), Options);

        var response = await client.QueryAsync("q");

        Assert.Equal(["first.test", "second.test", "first.test"], handler.Requests.Select(r => r.Uri.Host));
        Assert.Single(response.Elements);
    }

    [Fact]
    public async Task BadRequest_DoesNotRetry()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var client = new OverpassClient(new HttpClient(handler), Options);

        await Assert.ThrowsAsync<OverpassException>(() => client.QueryAsync("q"));
        Assert.Single(handler.Requests);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record SeenRequest(Uri Uri, string? UserAgent, string Body);

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SeenRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SeenRequest(request.RequestUri!, request.Headers.UserAgent.ToString(), body));
            return respond(request);
        }
    }
}
