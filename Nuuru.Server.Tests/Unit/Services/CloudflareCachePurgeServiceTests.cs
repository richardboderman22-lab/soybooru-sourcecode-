using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nuuru.Server.Services;

namespace Nuuru.Server.Tests.Unit.Services;

public class CloudflareCachePurgeServiceTests
{
    [Fact]
    public async Task PurgeUriAsync_RelativeUri_PurgesAllConfiguredDomainsInSingleRequest()
    {
        var requests = new List<HttpRequestMessage>();
        var service = CreateService(
            new CloudflareCachePurgeOptions
            {
                Domains = ["nuuru.example", "cdn.nuuru.example"],
                ZoneId = "zone-123",
                Token = "token-abc"
            },
            request =>
            {
                requests.Add(CloneRequest(request));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"success":true}""")
                };
            });

        var result = await service.PurgeUriAsync("/api/forum/threads/123");

        result.Should().BeTrue();
        requests.Should().HaveCount(1);

        var request = requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("https://api.cloudflare.com/client/v4/zones/zone-123/purge_cache"));
        request.Headers.Authorization?.Scheme.Should().Be("Bearer");
        request.Headers.Authorization?.Parameter.Should().Be("token-abc");

        var files = await ReadFilesAsync(request);
        files.Should().BeEquivalentTo(
        [
            "https://nuuru.example/api/forum/threads/123",
            "https://cdn.nuuru.example/api/forum/threads/123"
        ]);
    }

    [Fact]
    public async Task PurgeUriAsync_RelativeUriWithoutConfiguredDomains_ReturnsFalseWithoutSendingRequest()
    {
        var requestCount = 0;
        var service = CreateService(
            new CloudflareCachePurgeOptions
            {
                ZoneId = "zone-123",
                Token = "token-abc"
            },
            _ =>
            {
                requestCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"success":true}""")
                };
            });

        var result = await service.PurgeUriAsync("/api/forum/threads/123");

        result.Should().BeFalse();
        requestCount.Should().Be(0);
    }

    [Fact]
    public async Task PurgeUriAsync_AbsoluteUri_PurgesProvidedUrlWithoutConfiguredDomains()
    {
        var requests = new List<HttpRequestMessage>();
        var service = CreateService(
            new CloudflareCachePurgeOptions
            {
                ZoneId = "zone-123",
                Token = "token-abc"
            },
            request =>
            {
                requests.Add(CloneRequest(request));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"success":true}""")
                };
            });

        var result = await service.PurgeUriAsync("https://assets.nuuru.example/api/forum/threads/123");

        result.Should().BeTrue();
        requests.Should().HaveCount(1);
        var files = await ReadFilesAsync(requests.Single());
        files.Should().Equal("https://assets.nuuru.example/api/forum/threads/123");
    }

    private static CloudflareCachePurgeService CreateService(
        CloudflareCachePurgeOptions options,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler);
        var factory = new StubHttpClientFactory(httpClient);

        return new CloudflareCachePurgeService(
            factory,
            Options.Create(options),
            NullLogger<CloudflareCachePurgeService>.Instance);
    }

    private static async Task<IReadOnlyList<string>> ReadFilesAsync(HttpRequestMessage request)
    {
        var payload = await request.Content!.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        return document.RootElement
            .GetProperty("files")
            .EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content != null)
        {
            var content = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            clone.Content = new StringContent(content);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
