using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nuuru.Server.Data;
using Nuuru.Server.Models.Forum;
using Nuuru.Server.Services;
using Nuuru.Server.Services.BBCode;

namespace Nuuru.Server.Tests.Unit.Services;

public class ForumPostHtmlEnrichmentWorkerTests
{
    [Fact]
    public async Task Enqueue_StalePost_ProcessesDeferredEnrichment()
    {
        var databaseName = Guid.NewGuid().ToString();
        var enrichmentService = new FakeHtmlEnrichmentService();
        using var provider = BuildServiceProvider(databaseName, enrichmentService);
        var postId = await SeedPostAsync(provider, "first", "immediate::first", enrichmentService.ImmediateVersion);
        var worker = new ForumPostHtmlEnrichmentWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ForumPostHtmlEnrichmentWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            worker.Enqueue(postId);

            await WaitForAsync(async () =>
            {
                using var scope = provider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var post = await context.ForumPosts.SingleAsync(p => p.Id == postId);
                return post.ContentHtml == "enriched::parsed::first"
                    && post.ContentHtmlVersion == enrichmentService.CurrentVersion;
            });

            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var post = await context.ForumPosts.SingleAsync(p => p.Id == postId);

            post.ContentHtml.Should().Be("enriched::parsed::first");
            post.ContentHtmlVersion.Should().Be(enrichmentService.CurrentVersion);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Enqueue_DuringRunningEnrichment_RerunsForLatestContent()
    {
        var databaseName = Guid.NewGuid().ToString();
        var enrichmentService = new FakeHtmlEnrichmentService(blockFirstDeferredCall: true);
        using var provider = BuildServiceProvider(databaseName, enrichmentService);
        var postId = await SeedPostAsync(provider, "first", "immediate::first", enrichmentService.ImmediateVersion);
        var worker = new ForumPostHtmlEnrichmentWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ForumPostHtmlEnrichmentWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            worker.Enqueue(postId);
            await enrichmentService.FirstDeferredCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using (var scope = provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var post = await context.ForumPosts.SingleAsync(p => p.Id == postId);
                post.ContentRaw = "second";
                post.ContentHtml = "immediate::second";
                post.ContentHtmlVersion = enrichmentService.ImmediateVersion;
                await context.SaveChangesAsync();
            }

            worker.Enqueue(postId);
            enrichmentService.ReleaseFirstDeferredCall();

            await WaitForAsync(async () =>
            {
                using var scope = provider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var post = await context.ForumPosts.SingleAsync(p => p.Id == postId);
                return post.ContentRaw == "second"
                    && post.ContentHtml == "enriched::parsed::second"
                    && post.ContentHtmlVersion == enrichmentService.CurrentVersion;
            });

            using var verifyScope = provider.CreateScope();
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var updatedPost = await verifyContext.ForumPosts.SingleAsync(p => p.Id == postId);

            updatedPost.ContentRaw.Should().Be("second");
            updatedPost.ContentHtml.Should().Be("enriched::parsed::second");
            updatedPost.ContentHtmlVersion.Should().Be(enrichmentService.CurrentVersion);
            enrichmentService.DeferredInputs.Should().ContainInOrder("parsed::first", "parsed::second");
        }
        finally
        {
            enrichmentService.ReleaseFirstDeferredCall();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private static ServiceProvider BuildServiceProvider(string databaseName, FakeHtmlEnrichmentService enrichmentService)
    {
        var services = new ServiceCollection();

        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));

        var bbCodeService = new Mock<IBBCodeService>();
        bbCodeService
            .Setup(service => service.ParseWithMentions(It.IsAny<string>(), BBCodeContext.Forum, It.IsAny<Func<string, string?>?>()))
            .Returns((string raw, BBCodeContext _, Func<string, string?>? __) => new ParseResult($"parsed::{raw}", [], []));

        services.AddSingleton(bbCodeService.Object);
        services.AddSingleton<IHtmlEnrichmentService>(enrichmentService);

        return services.BuildServiceProvider();
    }

    private static async Task<int> SeedPostAsync(IServiceProvider provider, string contentRaw, string contentHtml, int contentHtmlVersion)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var post = new ForumPost
        {
            ThreadId = 1,
            AuthorId = Guid.NewGuid(),
            ContentRaw = contentRaw,
            ContentHtml = contentHtml,
            ContentHtmlVersion = contentHtmlVersion
        };

        context.ForumPosts.Add(post);
        await context.SaveChangesAsync();
        return post.Id;
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(50);
        }

        throw new TimeoutException("Condition was not met within the allotted time.");
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;

            await Task.Delay(50);
        }

        throw new TimeoutException("Condition was not met within the allotted time.");
    }

    private sealed class FakeHtmlEnrichmentService : IHtmlEnrichmentService
    {
        private readonly bool _blockFirstDeferredCall;
        private int _deferredCallCount;

        public FakeHtmlEnrichmentService(bool blockFirstDeferredCall = false)
        {
            _blockFirstDeferredCall = blockFirstDeferredCall;
        }

        public int ImmediateVersion => 1;
        public int CurrentVersion => 3;
        public int DeferredCallCount => Volatile.Read(ref _deferredCallCount);
        public List<string> DeferredInputs { get; } = [];
        public TaskCompletionSource<bool> FirstDeferredCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> FirstDeferredCallReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool NeedsEnrichment(string html, int contentHtmlVersion, HtmlEnrichmentContext context)
        {
            return !string.IsNullOrWhiteSpace(html) && contentHtmlVersion < CurrentVersion;
        }

        public Task<HtmlEnrichmentResult> EnrichImmediateAsync(string html, HtmlEnrichmentContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new HtmlEnrichmentResult(html, ImmediateVersion));
        }

        public async Task<HtmlEnrichmentResult> EnrichAsync(string html, HtmlEnrichmentContext context, CancellationToken cancellationToken = default)
        {
            var callNumber = Interlocked.Increment(ref _deferredCallCount);
            lock (DeferredInputs)
            {
                DeferredInputs.Add(html);
            }

            if (_blockFirstDeferredCall && callNumber == 1)
            {
                FirstDeferredCallStarted.TrySetResult(true);
                await FirstDeferredCallReleased.Task.WaitAsync(cancellationToken);
            }

            return new HtmlEnrichmentResult($"enriched::{html}", CurrentVersion);
        }

        public void ReleaseFirstDeferredCall()
        {
            FirstDeferredCallReleased.TrySetResult(true);
        }
    }
}
