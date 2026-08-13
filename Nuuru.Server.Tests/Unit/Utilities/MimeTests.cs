using FluentAssertions;
using Nuuru.Server.Utilities;

namespace Nuuru.Server.Tests.Unit.Utilities;

public class MimeTests
{
    [Theory]
    [InlineData(new byte[] { 0x46, 0x57, 0x53, 0x09 }, "movie.bin")]
    [InlineData(new byte[] { 0x43, 0x57, 0x53, 0x09 }, "movie.bin")]
    [InlineData(new byte[] { 0x5A, 0x57, 0x53, 0x09 }, "movie.bin")]
    public void DetectMIME_WithSwfHeader_ReturnsShockwaveFlash(byte[] bytes, string fileName)
    {
        var mimeType = MIME.DetectMIME(bytes, fileName);

        mimeType.Should().Be("application/x-shockwave-flash");
    }

    [Fact]
    public void DetectMIMEByExtension_WithSwfExtension_ReturnsShockwaveFlash()
    {
        var mimeType = MIME.DetectMIMEByExtension("movie.swf");

        mimeType.Should().Be("application/x-shockwave-flash");
    }
}
