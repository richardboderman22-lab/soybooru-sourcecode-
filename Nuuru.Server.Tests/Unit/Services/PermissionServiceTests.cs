using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Nuuru.Server.Auth;
using Nuuru.Server.Models;
using Nuuru.Server.Services;
using Nuuru.Server.Tests.Helpers;
using System.Security.Claims;

namespace Nuuru.Server.Tests.Unit.Services;

public class PermissionServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<RoleManager<ApplicationRole>> _mockRoleManager;
    private readonly Mock<ILogger<PermissionService>> _mockLogger;
    private readonly PermissionService _sut;

    public PermissionServiceTests()
    {
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null, null, null, null, null, null, null, null);

        var roleStore = new Mock<IRoleStore<ApplicationRole>>();
        _mockRoleManager = new Mock<RoleManager<ApplicationRole>>(
            roleStore.Object, null, null, null, null);

        _mockLogger = new Mock<ILogger<PermissionService>>();

        _sut = new PermissionService(_mockUserManager.Object, _mockRoleManager.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GrantPermissionAsync_WithValidUser_ReturnsTrue()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var permission = "User.UploadPost";

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.AddClaimAsync(user, It.Is<Claim>(c =>
                c.Type == Permissions.ClaimType && c.Value == permission)))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _sut.GrantPermissionAsync(user.Id, permission);

        // Assert
        result.Should().BeTrue();

        _mockUserManager.Verify(
            x => x.AddClaimAsync(user, It.Is<Claim>(c =>
                c.Type == Permissions.ClaimType && c.Value == permission)),
            Times.Once);
    }

    [Fact]
    public async Task GrantPermissionAsync_WithNonExistentUser_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permission = "User.UploadPost";

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _sut.GrantPermissionAsync(userId, permission);

        // Assert
        result.Should().BeFalse();

        _mockUserManager.Verify(
            x => x.AddClaimAsync(It.IsAny<ApplicationUser>(), It.IsAny<Claim>()),
            Times.Never);
    }

    [Fact]
    public async Task GrantPermissionAsync_WhenAddClaimFails_ReturnsFalse()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var permission = "User.UploadPost";

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.AddClaimAsync(user, It.IsAny<Claim>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Failed to add claim" }));

        // Act
        var result = await _sut.GrantPermissionAsync(user.Id, permission);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RevokePermissionAsync_WithValidUserAndPermission_ReturnsTrue()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var permission = "User.UploadPost";
        var existingClaim = new Claim(Permissions.ClaimType, permission);

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetClaimsAsync(user))
            .ReturnsAsync(new List<Claim> { existingClaim });

        _mockUserManager
            .Setup(x => x.RemoveClaimAsync(user, existingClaim))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _sut.RevokePermissionAsync(user.Id, permission);

        // Assert
        result.Should().BeTrue();

        _mockUserManager.Verify(
            x => x.RemoveClaimAsync(user, It.Is<Claim>(c =>
                c.Type == Permissions.ClaimType && c.Value == permission)),
            Times.Once);
    }

    [Fact]
    public async Task RevokePermissionAsync_WithNonExistentUser_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permission = "User.UploadPost";

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _sut.RevokePermissionAsync(userId, permission);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RevokePermissionAsync_WhenUserDoesNotHavePermission_ReturnsFalse()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var permission = "User.UploadPost";

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetClaimsAsync(user))
            .ReturnsAsync(new List<Claim>()); // No claims

        // Act
        var result = await _sut.RevokePermissionAsync(user.Id, permission);

        // Assert
        result.Should().BeFalse();

        _mockUserManager.Verify(
            x => x.RemoveClaimAsync(It.IsAny<ApplicationUser>(), It.IsAny<Claim>()),
            Times.Never);
    }

    [Fact]
    public async Task GetUserPermissionsAsync_WithValidUser_ReturnsPermissions()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var claims = new List<Claim>
        {
            new Claim(Permissions.ClaimType, "User.UploadPost"),
            new Claim(Permissions.ClaimType, "User.DeleteOwnPost"),
            new Claim("OtherClaimType", "SomeValue") // Should be filtered out
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetClaimsAsync(user))
            .ReturnsAsync(claims);

        // Act
        var result = await _sut.GetUserPermissionsAsync(user.Id);

        // Assert
        var permissions = result.ToList();
        permissions.Should().HaveCount(2);
        permissions.Should().Contain("User.UploadPost");
        permissions.Should().Contain("User.DeleteOwnPost");
        permissions.Should().NotContain("SomeValue");
    }

    [Fact]
    public async Task GetUserPermissionsAsync_WithNonExistentUser_ReturnsEmptyList()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _sut.GetUserPermissionsAsync(userId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task UserHasPermissionAsync_WhenUserHasPermission_ReturnsTrue()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var permission = "User.UploadPost";
        var claims = new List<Claim>
        {
            new Claim(Permissions.ClaimType, permission)
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetClaimsAsync(user))
            .ReturnsAsync(claims);

        // Act
        var result = await _sut.UserHasPermissionAsync(user.Id, permission);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task UserHasPermissionAsync_WhenUserDoesNotHavePermission_ReturnsFalse()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var permission = "Moderation.DeletePost";
        var claims = new List<Claim>
        {
            new Claim(Permissions.ClaimType, "User.UploadPost")
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetClaimsAsync(user))
            .ReturnsAsync(claims);

        // Act
        var result = await _sut.UserHasPermissionAsync(user.Id, permission);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetUserPermissionsAsync_ReplacesAllPermissions()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var existingClaims = new List<Claim>
        {
            new Claim(Permissions.ClaimType, "OldPermission1"),
            new Claim(Permissions.ClaimType, "OldPermission2")
        };
        var newPermissions = new[] { "NewPermission1", "NewPermission2", "NewPermission3" };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetClaimsAsync(user))
            .ReturnsAsync(existingClaims);

        _mockUserManager
            .Setup(x => x.RemoveClaimsAsync(user, It.IsAny<IEnumerable<Claim>>()))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager
            .Setup(x => x.AddClaimsAsync(user, It.IsAny<IEnumerable<Claim>>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _sut.SetUserPermissionsAsync(user.Id, newPermissions);

        // Assert
        result.Should().BeTrue();

        _mockUserManager.Verify(
            x => x.RemoveClaimsAsync(user, It.Is<IEnumerable<Claim>>(c => c.Count() == 2)),
            Times.Once);

        _mockUserManager.Verify(
            x => x.AddClaimsAsync(user, It.Is<IEnumerable<Claim>>(c =>
                c.Count() == 3 &&
                c.All(claim => claim.Type == Permissions.ClaimType) &&
                c.Any(claim => claim.Value == "NewPermission1") &&
                c.Any(claim => claim.Value == "NewPermission2") &&
                c.Any(claim => claim.Value == "NewPermission3"))),
            Times.Once);
    }

    [Fact]
    public async Task SetUserPermissionsAsync_WithNonExistentUser_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissions = new[] { "SomePermission" };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _sut.SetUserPermissionsAsync(userId, permissions);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasPermission_WithAuthenticatedUserAndPermission_ReturnsTrue()
    {
        // Arrange
        var permission = "User.UploadPost";
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(Permissions.ClaimType, permission)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        // Act
        var result = _sut.HasPermission(claimsPrincipal, permission);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void HasPermission_WithAuthenticatedUserWithoutPermission_ReturnsFalse()
    {
        // Arrange
        var permission = "Moderation.DeletePost";
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(Permissions.ClaimType, "User.UploadPost")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        // Act
        var result = _sut.HasPermission(claimsPrincipal, permission);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasPermission_WithUnauthenticatedUser_ReturnsFalse()
    {
        // Arrange
        var claimsPrincipal = new ClaimsPrincipal();
        var permission = "User.UploadPost";

        // Act
        var result = _sut.HasPermission(claimsPrincipal, permission);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasPermission_WithNullUser_ReturnsFalse()
    {
        // Arrange
        var permission = "User.UploadPost";

        // Act
        var result = _sut.HasPermission(null!, permission);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DenyPermissionAsync_WithValidUser_ReturnsTrue()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var permission = Permissions.User.DeleteOwnContent;

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetClaimsAsync(user))
            .ReturnsAsync(new List<Claim>());

        _mockUserManager
            .Setup(x => x.AddClaimAsync(user, It.Is<Claim>(c =>
                c.Type == Permissions.DenyClaimType && c.Value == permission)))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _sut.DenyPermissionAsync(user.Id, permission);

        // Assert
        result.Should().BeTrue();

        _mockUserManager.Verify(
            x => x.AddClaimAsync(user, It.Is<Claim>(c =>
                c.Type == Permissions.DenyClaimType && c.Value == permission)),
            Times.Once);
    }

    [Fact]
    public async Task DenyPermissionAsync_WhenAlreadyDenied_ReturnsFalse()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var permission = Permissions.User.DeleteOwnContent;
        var existingClaims = new List<Claim>
        {
            new Claim(Permissions.DenyClaimType, permission)
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetClaimsAsync(user))
            .ReturnsAsync(existingClaims);

        // Act
        var result = await _sut.DenyPermissionAsync(user.Id, permission);

        // Assert
        result.Should().BeFalse();

        _mockUserManager.Verify(
            x => x.AddClaimAsync(It.IsAny<ApplicationUser>(), It.IsAny<Claim>()),
            Times.Never);
    }

    [Fact]
    public async Task RemoveDenyPermissionAsync_WithValidUser_ReturnsTrue()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var permission = Permissions.User.DeleteOwnContent;
        var denyClaim = new Claim(Permissions.DenyClaimType, permission);

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetClaimsAsync(user))
            .ReturnsAsync(new List<Claim> { denyClaim });

        _mockUserManager
            .Setup(x => x.RemoveClaimAsync(user, denyClaim))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _sut.RemoveDenyPermissionAsync(user.Id, permission);

        // Assert
        result.Should().BeTrue();

        _mockUserManager.Verify(
            x => x.RemoveClaimAsync(user, It.Is<Claim>(c =>
                c.Type == Permissions.DenyClaimType && c.Value == permission)),
            Times.Once);
    }

    [Fact]
    public async Task RemoveDenyPermissionAsync_WhenNotDenied_ReturnsFalse()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var permission = Permissions.User.DeleteOwnContent;

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetClaimsAsync(user))
            .ReturnsAsync(new List<Claim>());

        // Act
        var result = await _sut.RemoveDenyPermissionAsync(user.Id, permission);

        // Assert
        result.Should().BeFalse();

        _mockUserManager.Verify(
            x => x.RemoveClaimAsync(It.IsAny<ApplicationUser>(), It.IsAny<Claim>()),
            Times.Never);
    }

    [Fact]
    public async Task GetDeniedPermissionsAsync_WithValidUser_ReturnsDeniedPermissions()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var claims = new List<Claim>
        {
            new Claim(Permissions.DenyClaimType, Permissions.User.DeleteOwnContent),
            new Claim(Permissions.DenyClaimType, Permissions.User.UploadPost),
            new Claim(Permissions.ClaimType, Permissions.User.Comment) // Should be filtered out
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetClaimsAsync(user))
            .ReturnsAsync(claims);

        // Act
        var result = await _sut.GetDeniedPermissionsAsync(user.Id);

        // Assert
        var deniedPermissions = result.ToList();
        deniedPermissions.Should().HaveCount(2);
        deniedPermissions.Should().Contain(Permissions.User.DeleteOwnContent);
        deniedPermissions.Should().Contain(Permissions.User.UploadPost);
        deniedPermissions.Should().NotContain(Permissions.User.Comment);
    }

    [Fact]
    public async Task GetEffectivePermissionsAsync_WithRoleAndUserPermissions_ReturnsUnion()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var roleName = "User";
        var role = new ApplicationRole { Id = Guid.NewGuid(), Name = roleName };

        var roleClaims = new List<Claim>
        {
            new Claim(Permissions.ClaimType, Permissions.User.UploadPost),
            new Claim(Permissions.ClaimType, Permissions.User.Comment)
        };

        var userClaims = new List<Claim>
        {
            new Claim(Permissions.ClaimType, Permissions.User.EditOwnContent)
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { roleName });

        _mockRoleManager
            .Setup(x => x.FindByNameAsync(roleName))
            .ReturnsAsync(role);

        _mockRoleManager
            .Setup(x => x.GetClaimsAsync(role))
            .ReturnsAsync(roleClaims);

        _mockUserManager
            .Setup(x => x.GetClaimsAsync(user))
            .ReturnsAsync(userClaims);

        // Act
        var result = await _sut.GetEffectivePermissionsAsync(user.Id);

        // Assert
        var effectivePermissions = result.ToList();
        effectivePermissions.Should().HaveCount(3);
        effectivePermissions.Should().Contain(Permissions.User.UploadPost);
        effectivePermissions.Should().Contain(Permissions.User.Comment);
        effectivePermissions.Should().Contain(Permissions.User.EditOwnContent);
    }

    [Fact]
    public async Task GetEffectivePermissionsAsync_WithDenials_ExcludesDeniedPermissions()
    {
        // Arrange
        var user = MockData.CreateTestUser();
        var roleName = "User";
        var role = new ApplicationRole { Id = Guid.NewGuid(), Name = roleName };

        var roleClaims = new List<Claim>
        {
            new Claim(Permissions.ClaimType, Permissions.User.UploadPost),
            new Claim(Permissions.ClaimType, Permissions.User.Comment),
            new Claim(Permissions.ClaimType, Permissions.User.DeleteOwnContent)
        };

        var userClaims = new List<Claim>
        {
            new Claim(Permissions.ClaimType, Permissions.User.EditOwnContent),
            new Claim(Permissions.DenyClaimType, Permissions.User.DeleteOwnContent) // Deny this one
        };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { roleName });

        _mockRoleManager
            .Setup(x => x.FindByNameAsync(roleName))
            .ReturnsAsync(role);

        _mockRoleManager
            .Setup(x => x.GetClaimsAsync(role))
            .ReturnsAsync(roleClaims);

        _mockUserManager
            .Setup(x => x.GetClaimsAsync(user))
            .ReturnsAsync(userClaims);

        // Act
        var result = await _sut.GetEffectivePermissionsAsync(user.Id);

        // Assert
        var effectivePermissions = result.ToList();
        effectivePermissions.Should().HaveCount(3); // Should NOT include DeleteOwnContent
        effectivePermissions.Should().Contain(Permissions.User.UploadPost);
        effectivePermissions.Should().Contain(Permissions.User.Comment);
        effectivePermissions.Should().Contain(Permissions.User.EditOwnContent);
        effectivePermissions.Should().NotContain(Permissions.User.DeleteOwnContent); // Denied!
    }
}
