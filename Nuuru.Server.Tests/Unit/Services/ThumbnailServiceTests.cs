using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.Services;
using Nuuru.Server.Services.Storage;

namespace Nuuru.Server.Tests.Unit.Services;

public class ThumbnailServiceTests
{
    [Fact]
    public void SupportsThumbnail_WithSwfMimeType_ReturnsTrue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        var service = new ThumbnailService(
            new Mock<IFileStorageService>().Object,
            configuration,
            new Mock<ILogger<ThumbnailService>>().Object);

        var supported = service.SupportsThumbnail("application/x-shockwave-flash");

        supported.Should().BeTrue();
    }
}
