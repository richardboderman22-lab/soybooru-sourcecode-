using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nuuru.Server.Data;
using Nuuru.Server.Models.Forum;
using Nuuru.Server.Services;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class HtmlEnrichmentServiceTests
{
    [Fact]
    public async Task EnrichAsync_StandaloneBareLink_AddsPreviewMarkup()
    {
        using var context = TestDbContextFactory.Create();
        var service = CreateService(context, request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            return path switch
            {
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        <html>
                          <head>
                            <title>Fallback Title</title>
                            <meta property="og:title" content="Example Title" />
                            <meta property="og:description" content="Example description" />
                            <meta property="og:site_name" content="Example Site" />
                            <meta property="og:image" content="/og.png" />
                            <link rel="icon" href="/favicon.png" />
                          </head>
                        </html>
                        """, Encoding.UTF8, "text/html")
                }
            };
        }, new FakeRemoteImageInfoService());

        var html = "<a href=\"https://93.184.216.34/post\" class=\"bbcode-link\" rel=\"nofollow noopener\" target=\"_blank\">https://93.184.216.34/post</a>";

        var enriched = await service.EnrichAsync(html, HtmlEnrichmentContext.ForForumPost());

        enriched.Version.Should().Be(service.CurrentVersion);
        enriched.Html.Should().Contain("bbcode-link-preview-card");
        enriched.Html.Should().Contain("Example Title");
        enriched.Html.Should().Contain("Example description");
        enriched.Html.Should().Contain("data:image/png;base64,");
    }

    [Fact]
    public async Task EnrichImmediateAsync_StandaloneBareLink_DoesNotAddPreviewMarkup()
    {
        using var context = TestDbContextFactory.Create();
        var service = CreateService(context, _ => throw new InvalidOperationException("Deferred fetch should not run during immediate enrichment"));
        var html = "<a href=\"https://93.184.216.34/post\" class=\"bbcode-link\" rel=\"nofollow noopener\" target=\"_blank\">https://93.184.216.34/post</a>";

        var enriched = await service.EnrichImmediateAsync(html, HtmlEnrichmentContext.ForForumPost());

        enriched.Version.Should().Be(service.ImmediateVersion);
        enriched.Html.Should().NotContain("bbcode-link-preview-card");
    }

    [Fact]
    public async Task EnrichAsync_AttachmentImageWithoutIntrinsicSize_AddsWidthAndHeightAttributes()
    {
        using var context = TestDbContextFactory.Create();
        var attachmentId = Guid.NewGuid();

        context.ForumPostAttachments.Add(new ForumPostAttachment
        {
            Id = attachmentId,
            UploaderId = Guid.NewGuid(),
            FileIdentifier = "file-id",
            OriginalFileName = "image.png",
            ContentType = "image/png",
            FileSize = 1024,
            Width = 640,
            Height = 480
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, _ => throw new InvalidOperationException("Should not fetch remote metadata for attachment dimensions"));
        var html = $"<a href=\"/api/forum/attachments/{attachmentId}/file\" class=\"bbcode-attachment\" target=\"_blank\"><img src=\"/api/forum/attachments/{attachmentId}/file\" loading=\"lazy\" data-attachment-id=\"{attachmentId}\" /></a>";

        var enriched = await service.EnrichAsync(html, HtmlEnrichmentContext.ForForumPost());

        enriched.Html.Should().Contain("width=\"640\"");
        enriched.Html.Should().Contain("height=\"480\"");
    }

    private static HtmlEnrichmentService CreateService(
        ApplicationDbContext context,
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        IRemoteImageInfoService? remoteImageInfoService = null)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler);
        var factory = new StubHttpClientFactory(httpClient);
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        remoteImageInfoService ??= new RemoteImageInfoService(factory, memoryCache, NullLogger<RemoteImageInfoService>.Instance);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Thumbnails:MaxWidth"] = "300",
                ["Thumbnails:MaxHeight"] = "300"
            })
            .Build();

        return new HtmlEnrichmentService(
        [
            new EmbeddedImageDimensionsHtmlEnricher(context, configuration),
            new StandaloneLinkPreviewHtmlEnricher(factory, remoteImageInfoService, memoryCache, NullLogger<StandaloneLinkPreviewHtmlEnricher>.Instance)
        ]);
    }

    private static HttpResponseMessage CreateBinaryResponse(string contentType, byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return response;
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

    private sealed class FakeRemoteImageInfoService : IRemoteImageInfoService
    {
        public Task<RemoteImageInfo?> GetAsync(string? url, int maxBytes, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<RemoteImageInfo?>(new RemoteImageInfo("image/png", "data:image/png;base64,AAAA", 1, 1));
        }
    }
}
