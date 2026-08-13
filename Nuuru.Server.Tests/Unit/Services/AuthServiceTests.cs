using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.Models;
using Nuuru.Server.Services;
using Nuuru.Server.Tests.Helpers;

namespace Nuuru.Server.Tests.Unit.Services;

public class AuthServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<SignInManager<ApplicationUser>> _mockSignInManager;
    private readonly Mock<ITokenService> _mockTokenService;
    private readonly Mock<ISiteSettingsService> _mockSiteSettingsService;
    private readonly Mock<IIpIntelligenceService> _mockIpIntelligenceService;
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null, null, null, null, null, null, null, null);

        var contextAccessor = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        _mockSignInManager = new Mock<SignInManager<ApplicationUser>>(
            _mockUserManager.Object,
            contextAccessor.Object,
            claimsFactory.Object,
            null, null, null, null);

        _mockTokenService = new Mock<ITokenService>();
        _mockSiteSettingsService = new Mock<ISiteSettingsService>();
        _mockIpIntelligenceService = new Mock<IIpIntelligenceService>();
        _mockLogger = new Mock<ILogger<AuthService>>();

        _mockSiteSettingsService
            .Setup(x => x.IsSignupGeoVerificationEnabledAsync())
            .ReturnsAsync(true);

        _mockSiteSettingsService
            .Setup(x => x.GetSignupGeoVerificationHoldHoursAsync())
            .ReturnsAsync(24);

        _mockSiteSettingsService
            .Setup(x => x.ShouldRejectFlaggedVpnSignupsAsync())
            .ReturnsAsync(true);

        _mockSiteSettingsService
            .Setup(x => x.GetSignupGeoVerificationLookupUrlAsync())
            .ReturnsAsync("http://localhost:8080/ip");

        _mockIpIntelligenceService
            .Setup(x => x.LookupAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IpIntelligenceResult
            {
                IpAddress = "146.70.61.131",
                IsFlagged = false,
                CountryCode = "gb",
                RegionCode = "eng",
                City = "london",
                IspAsn = "AS9009",
                IspName = "M247 Ltd"
            });

        _mockUserManager
            .Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string> { "User" });

        var dbContextOptions = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Nuuru.Server.Data.ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
            .Options;
        var dbContext = new Nuuru.Server.Data.ApplicationDbContext(dbContextOptions);

        _sut = new AuthService(
            _mockUserManager.Object,
            _mockSignInManager.Object,
            _mockTokenService.Object,
            _mockSiteSettingsService.Object,
            _mockIpIntelligenceService.Object,
            dbContext,
            _mockLogger.Object);
    }

    [Fact]
    public async Task RegisterAsync_WithValidCredentials_ReturnsPendingVerification()
    {
        var userName = "newuser";
        var password = "SecurePassword123!";

        _mockUserManager
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), password))
            .ReturnsAsync(IdentityResult.Success);

        var result = await _sut.RegisterAsync(userName, password, "146.70.61.131");

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.RequiresVerification.Should().BeTrue();
        result.RefreshToken.Should().BeNull();
        result.VerificationAvailableAt.Should().NotBeNull();

        _mockUserManager.Verify(
            x => x.CreateAsync(It.Is<ApplicationUser>(u =>
                u.UserName == userName &&
                u.IsGeoVerificationPending), password),
            Times.Once);

        _mockTokenService.Verify(
            x => x.GenerateRefreshTokenAsync(It.IsAny<ApplicationUser>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WhenIpIsFlagged_ReturnsFailure()
    {
        _mockIpIntelligenceService
            .Setup(x => x.LookupAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IpIntelligenceResult
            {
                IpAddress = "146.70.61.131",
                IsFlagged = true,
                CountryCode = "gb",
                RegionCode = "eng",
                City = "london",
                IspAsn = "AS9009",
                IspName = "M247 Ltd"
            });

        var result = await _sut.RegisterAsync("newuser", "SecurePassword123!", "146.70.61.131");

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors![0].Should().Contain("VPN");

        _mockUserManager.Verify(
            x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WhenUserCreationFails_ReturnsFailureWithErrors()
    {
        var userName = "newuser";
        var password = "weak";

        var errors = new[]
        {
            new IdentityError { Description = "Password too weak" },
            new IdentityError { Description = "Username already taken" }
        };

        _mockUserManager
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), password))
            .ReturnsAsync(IdentityResult.Failed(errors));

        var result = await _sut.RegisterAsync(userName, password, "146.70.61.131");

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain("Password too weak");
        result.Errors.Should().Contain("Username already taken");

        _mockTokenService.Verify(
            x => x.GenerateRefreshTokenAsync(It.IsAny<ApplicationUser>()),
            Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsSuccessWithToken()
    {
        var userName = "testuser";
        var password = "CorrectPassword123!";
        var expectedRefreshToken = "refresh-token-456";
        var user = MockData.CreateTestUser(userName);

        _mockSignInManager
            .Setup(x => x.PasswordSignInAsync(userName, password, false, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        _mockUserManager
            .Setup(x => x.FindByNameAsync(userName))
            .ReturnsAsync(user);

        _mockTokenService
            .Setup(x => x.GenerateRefreshTokenAsync(user))
            .ReturnsAsync(expectedRefreshToken);

        var result = await _sut.LoginAsync(userName, password, "146.70.61.131");

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.RefreshToken.Should().Be(expectedRefreshToken);
    }

    [Fact]
    public async Task LoginAsync_WithInvalidCredentials_ReturnsFailure()
    {
        var userName = "testuser";
        var password = "WrongPassword";

        _mockSignInManager
            .Setup(x => x.PasswordSignInAsync(userName, password, false, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        var result = await _sut.LoginAsync(userName, password, "146.70.61.131");

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain("Invalid credentials");

        _mockTokenService.Verify(
            x => x.GenerateRefreshTokenAsync(It.IsAny<ApplicationUser>()),
            Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenPendingVerificationIpDoesNotMatch_ReturnsRetryableFailure()
    {
        var userName = "pendinguser";
        var password = "CorrectPassword123!";
        var user = MockData.CreateTestUser(userName);
        user.IsGeoVerificationPending = true;
        user.SignupVerificationAvailableAt = DateTime.UtcNow.AddHours(-1);
        user.SignupVerificationCountryCode = "gb";
        user.SignupVerificationRegionCode = "eng";
        user.SignupVerificationCity = "london";
        user.SignupVerificationIspAsn = "AS9009";

        _mockSignInManager
            .Setup(x => x.PasswordSignInAsync(userName, password, false, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        _mockUserManager
            .Setup(x => x.FindByNameAsync(userName))
            .ReturnsAsync(user);

        _mockIpIntelligenceService
            .Setup(x => x.LookupAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IpIntelligenceResult
            {
                IpAddress = "203.0.113.22",
                IsFlagged = false,
                CountryCode = "us",
                RegionCode = "ca",
                City = "san francisco",
                IspAsn = "AS7922",
                IspName = "Comcast Cable"
            });

        var result = await _sut.LoginAsync(userName, password, "203.0.113.22");

        result.Success.Should().BeFalse();
        result.CanRetryIpVerification.Should().BeTrue();
        result.Errors.Should().Contain("Login IP did not match your signup region and ISP.");
    }

    [Fact]
    public async Task LoginAsync_WhenAccountIsLockedOut_ReturnsFailure()
    {
        var userName = "lockeduser";
        var password = "SomePassword";
        var user = MockData.CreateTestUser(userName, "locked@example.com");

        _mockUserManager
            .Setup(x => x.FindByNameAsync(userName))
            .ReturnsAsync(user);

        _mockSignInManager
            .Setup(x => x.PasswordSignInAsync(userName, password, false, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.LockedOut);

        var result = await _sut.LoginAsync(userName, password, "146.70.61.131");

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain("Account is locked");
    }

    [Fact]
    public async Task RegisterAsync_WithCharterIp_StoresVerificationFieldsAndReturns24hHold()
    {
        _mockIpIntelligenceService
            .Setup(x => x.LookupAsync("147.0.1.1", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IpIntelligenceResult
            {
                IpAddress = "147.0.1.1",
                IsFlagged = false,
                CountryCode = "us",
                RegionCode = "oh",
                Region = "Ohio",
                City = "Fremont",
                IspAsn = "AS10796",
                IspName = "Charter Communications Inc"
            });

        ApplicationUser? capturedUser = null;
        _mockUserManager
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .Callback<ApplicationUser, string>((u, _) => capturedUser = u)
            .ReturnsAsync(IdentityResult.Success);

        var result = await _sut.RegisterAsync("charteruser", "SecurePassword123!", "147.0.1.1");

        result.Success.Should().BeTrue();
        result.RequiresVerification.Should().BeTrue();
        result.VerificationAvailableAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));

        capturedUser.Should().NotBeNull();
        capturedUser!.IsGeoVerificationPending.Should().BeTrue();
        capturedUser.SignupVerificationIpAddress.Should().Be("147.0.1.1");
        capturedUser.SignupVerificationCountryCode.Should().Be("us");
        capturedUser.SignupVerificationRegionCode.Should().Be("oh");
        capturedUser.SignupVerificationCity.Should().Be("Fremont");
        capturedUser.SignupVerificationIspAsn.Should().Be("AS10796");
        capturedUser.SignupVerificationIspName.Should().Be("Charter Communications Inc");
    }

    [Fact]
    public async Task LoginAsync_DuringHoldPeriod_ReturnsFailure()
    {
        var userName = "holduser";
        var password = "SecurePassword123!";
        var user = MockData.CreateTestUser(userName);
        user.IsGeoVerificationPending = true;
        user.SignupVerificationAvailableAt = DateTime.UtcNow.AddHours(12); // 12h remaining

        _mockSignInManager
            .Setup(x => x.PasswordSignInAsync(userName, password, false, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        _mockUserManager
            .Setup(x => x.FindByNameAsync(userName))
            .ReturnsAsync(user);

        var result = await _sut.LoginAsync(userName, password, "147.0.1.1");

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("verification hold is active");
    }

    [Fact]
    public async Task LoginAsync_DuringHoldPeriod_WithNonDefaultRole_BypassesVerificationAndActivatesAccount()
    {
        var userName = "trustedholduser";
        var password = "SecurePassword123!";
        var expectedToken = "refresh-token-trusted";
        var user = MockData.CreateTestUser(userName);
        user.IsGeoVerificationPending = true;
        user.SignupVerificationAvailableAt = DateTime.UtcNow.AddHours(12);
        user.SignupVerificationCountryCode = "gb";
        user.SignupVerificationRegionCode = "eng";
        user.SignupVerificationCity = "london";
        user.SignupVerificationIspAsn = "AS9009";
        user.SignupVerificationIspName = "M247 Ltd";

        _mockSignInManager
            .Setup(x => x.PasswordSignInAsync(userName, password, false, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        _mockUserManager
            .Setup(x => x.FindByNameAsync(userName))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "User", "Trusted" });

        _mockUserManager
            .Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        _mockTokenService
            .Setup(x => x.GenerateRefreshTokenAsync(user))
            .ReturnsAsync(expectedToken);

        var result = await _sut.LoginAsync(userName, password, null);

        result.Success.Should().BeTrue();
        result.RefreshToken.Should().Be(expectedToken);
        user.IsGeoVerificationPending.Should().BeFalse();
        user.SignupVerificationCompletedAt.Should().NotBeNull();

        _mockIpIntelligenceService.Verify(
            x => x.LookupAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LoginAsync_AfterHoldWithMatchingCharterIp_ActivatesAccount()
    {
        var userName = "charteruser";
        var password = "SecurePassword123!";
        var expectedToken = "refresh-token-charter";
        var user = MockData.CreateTestUser(userName);
        user.IsGeoVerificationPending = true;
        user.SignupVerificationAvailableAt = DateTime.UtcNow.AddHours(-1); // hold expired
        user.SignupVerificationCountryCode = "us";
        user.SignupVerificationRegionCode = "oh";
        user.SignupVerificationCity = "Fremont";
        user.SignupVerificationIspAsn = "AS10796";
        user.SignupVerificationIspName = "Charter Communications Inc";

        _mockSignInManager
            .Setup(x => x.PasswordSignInAsync(userName, password, false, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        _mockUserManager
            .Setup(x => x.FindByNameAsync(userName))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        _mockIpIntelligenceService
            .Setup(x => x.LookupAsync("147.0.1.1", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IpIntelligenceResult
            {
                IpAddress = "147.0.1.1",
                IsFlagged = false,
                CountryCode = "us",
                RegionCode = "oh",
                Region = "Ohio",
                City = "Fremont",
                IspAsn = "AS10796",
                IspName = "Charter Communications Inc"
            });

        _mockTokenService
            .Setup(x => x.GenerateRefreshTokenAsync(user))
            .ReturnsAsync(expectedToken);

        var result = await _sut.LoginAsync(userName, password, "147.0.1.1");

        result.Success.Should().BeTrue();
        result.RefreshToken.Should().Be(expectedToken);
        user.IsGeoVerificationPending.Should().BeFalse();
        user.SignupVerificationCompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task LoginAsync_DuringHoldPeriod_WithNoRoles_DoesNotBypassHold()
    {
        var userName = "noroleuser";
        var password = "SecurePassword123!";
        var user = MockData.CreateTestUser(userName);
        user.IsGeoVerificationPending = true;
        user.SignupVerificationAvailableAt = DateTime.UtcNow.AddHours(12);
        user.SignupVerificationCountryCode = "gb";
        user.SignupVerificationRegionCode = "eng";
        user.SignupVerificationCity = "london";
        user.SignupVerificationIspAsn = "AS9009";
        user.SignupVerificationIspName = "M247 Ltd";

        _mockSignInManager
            .Setup(x => x.PasswordSignInAsync(userName, password, false, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        _mockUserManager
            .Setup(x => x.FindByNameAsync(userName))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string>());

        var result = await _sut.LoginAsync(userName, password, "146.70.61.131");

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("verification hold is active");
        user.IsGeoVerificationPending.Should().BeTrue();
        user.SignupVerificationCompletedAt.Should().BeNull();

        _mockUserManager.Verify(
            x => x.UpdateAsync(It.IsAny<ApplicationUser>()),
            Times.Never);
        _mockTokenService.Verify(
            x => x.GenerateRefreshTokenAsync(user),
            Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WithNullIp_WhenVerificationEnabled_ReturnsFailure()
    {
        var result = await _sut.RegisterAsync("newuser", "SecurePassword123!", null);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("IP address");

        _mockUserManager.Verify(
            x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WhenLookupReturnsNull_ReturnsFailure()
    {
        _mockIpIntelligenceService
            .Setup(x => x.LookupAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IpIntelligenceResult?)null);

        var result = await _sut.RegisterAsync("newuser", "SecurePassword123!", "10.0.0.1");

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("validate your IP");

        _mockUserManager.Verify(
            x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WhenVerificationDisabled_ReturnsActiveWithToken()
    {
        _mockSiteSettingsService
            .Setup(x => x.IsSignupGeoVerificationEnabledAsync())
            .ReturnsAsync(false);

        _mockUserManager
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        _mockTokenService
            .Setup(x => x.GenerateRefreshTokenAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync("refresh-token-direct");

        var result = await _sut.RegisterAsync("directuser", "SecurePassword123!", "147.0.1.1");

        result.Success.Should().BeTrue();
        result.RequiresVerification.Should().BeFalse();
        result.RefreshToken.Should().Be("refresh-token-direct");

        _mockUserManager.Verify(
            x => x.CreateAsync(It.Is<ApplicationUser>(u => !u.IsGeoVerificationPending), It.IsAny<string>()),
            Times.Once);

        _mockIpIntelligenceService.Verify(
            x => x.LookupAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
