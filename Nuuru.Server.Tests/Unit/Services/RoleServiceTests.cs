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

public class RoleServiceTests
{
    private readonly Mock<RoleManager<ApplicationRole>> _mockRoleManager;
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<ILogger<RoleService>> _mockLogger;
    private readonly RoleService _sut;

    public RoleServiceTests()
    {
        var roleStore = new Mock<IRoleStore<ApplicationRole>>();
        _mockRoleManager = new Mock<RoleManager<ApplicationRole>>(
            roleStore.Object, null, null, null, null);

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null, null, null, null, null, null, null, null);

        _mockLogger = new Mock<ILogger<RoleService>>();

        _sut = new RoleService(_mockRoleManager.Object, _mockUserManager.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task CreateRoleAsync_WithValidData_ReturnsRoleDto()
    {
        // Arrange
        var roleName = "TestRole";
        var color = "#3b82f6";
        var permissions = new[] { Permissions.User.UploadPost, Permissions.User.Comment };
        var roleId = Guid.NewGuid();

        _mockRoleManager
            .Setup(x => x.FindByNameAsync(roleName))
            .ReturnsAsync((ApplicationRole?)null);

        _mockRoleManager
            .Setup(x => x.CreateAsync(It.IsAny<ApplicationRole>()))
            .ReturnsAsync(IdentityResult.Success)
            .Callback<ApplicationRole>(r => r.Id = roleId);

        _mockRoleManager
            .Setup(x => x.AddClaimAsync(It.IsAny<ApplicationRole>(), It.IsAny<Claim>()))
            .ReturnsAsync(IdentityResult.Success);

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(new ApplicationRole { Id = roleId, Name = roleName, Color = color });

        _mockRoleManager
            .Setup(x => x.GetClaimsAsync(It.IsAny<ApplicationRole>()))
            .ReturnsAsync(permissions.Select(p => new Claim(Permissions.ClaimType, p)).ToList());

        _mockUserManager
            .Setup(x => x.GetUsersInRoleAsync(roleName))
            .ReturnsAsync(new List<ApplicationUser>());

        // Act
        var result = await _sut.CreateRoleAsync(roleName, color, permissions);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be(roleName);
        result.Color.Should().Be(color);
        result.Permissions.Should().BeEquivalentTo(permissions);

        _mockRoleManager.Verify(
            x => x.CreateAsync(It.Is<ApplicationRole>(r => r.Name == roleName && r.Color == color)),
            Times.Once);

        _mockRoleManager.Verify(
            x => x.AddClaimAsync(It.IsAny<ApplicationRole>(), It.IsAny<Claim>()),
            Times.Exactly(permissions.Length));
    }

    [Fact]
    public async Task CreateRoleAsync_WithEmptyName_ReturnsNull()
    {
        // Arrange
        var roleName = "";
        var color = "#3b82f6";
        var permissions = new[] { Permissions.User.UploadPost };

        // Act
        var result = await _sut.CreateRoleAsync(roleName, color, permissions);

        // Assert
        result.Should().BeNull();

        _mockRoleManager.Verify(
            x => x.CreateAsync(It.IsAny<ApplicationRole>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateRoleAsync_WithExistingRoleName_ReturnsNull()
    {
        // Arrange
        var roleName = "ExistingRole";
        var color = "#3b82f6";
        var permissions = new[] { Permissions.User.UploadPost };
        var existingRole = new ApplicationRole { Id = Guid.NewGuid(), Name = roleName };

        _mockRoleManager
            .Setup(x => x.FindByNameAsync(roleName))
            .ReturnsAsync(existingRole);

        // Act
        var result = await _sut.CreateRoleAsync(roleName, color, permissions);

        // Assert
        result.Should().BeNull();

        _mockRoleManager.Verify(
            x => x.CreateAsync(It.IsAny<ApplicationRole>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateRoleAsync_WithValidData_ReturnsUpdatedRoleDto()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var oldName = "OldRole";
        var newName = "NewRole";
        var oldColor = "#ff0000";
        var newColor = "#00ff00";
        var newPermissions = new[] { Permissions.Moderation.TrashPost };
        var role = new ApplicationRole { Id = roleId, Name = oldName, Color = oldColor };

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(role);

        _mockRoleManager
            .Setup(x => x.FindByNameAsync(newName))
            .ReturnsAsync((ApplicationRole?)null);

        _mockRoleManager
            .Setup(x => x.UpdateAsync(It.IsAny<ApplicationRole>()))
            .ReturnsAsync(IdentityResult.Success);

        _mockRoleManager
            .Setup(x => x.GetClaimsAsync(role))
            .ReturnsAsync(new List<Claim> { new Claim(Permissions.ClaimType, "old.permission") });

        _mockRoleManager
            .Setup(x => x.RemoveClaimAsync(It.IsAny<ApplicationRole>(), It.IsAny<Claim>()))
            .ReturnsAsync(IdentityResult.Success);

        _mockRoleManager
            .Setup(x => x.AddClaimAsync(It.IsAny<ApplicationRole>(), It.IsAny<Claim>()))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager
            .Setup(x => x.GetUsersInRoleAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<ApplicationUser>());

        // Act
        var result = await _sut.UpdateRoleAsync(roleId, newName, newColor, newPermissions);

        // Assert
        result.Should().NotBeNull();
        role.Name.Should().Be(newName);
        role.Color.Should().Be(newColor);

        _mockRoleManager.Verify(
            x => x.UpdateAsync(It.Is<ApplicationRole>(r => r.Name == newName && r.Color == newColor)),
            Times.Once);

        _mockRoleManager.Verify(
            x => x.AddClaimAsync(It.IsAny<ApplicationRole>(),
                It.Is<Claim>(c => c.Type == Permissions.ClaimType && c.Value == Permissions.Moderation.TrashPost)),
            Times.Once);
    }

    [Fact]
    public async Task UpdateRoleAsync_WithNonExistentRole_ReturnsNull()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var newName = "NewRole";
        var newColor = "#3b82f6";
        var permissions = new[] { Permissions.User.UploadPost };

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync((ApplicationRole?)null);

        // Act
        var result = await _sut.UpdateRoleAsync(roleId, newName, newColor, permissions);

        // Assert
        result.Should().BeNull();

        _mockRoleManager.Verify(
            x => x.UpdateAsync(It.IsAny<ApplicationRole>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteRoleAsync_WithValidRole_ReturnsTrue()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var roleName = "TestRole";
        var role = new ApplicationRole { Id = roleId, Name = roleName };

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(role);

        _mockUserManager
            .Setup(x => x.GetUsersInRoleAsync(roleName))
            .ReturnsAsync(new List<ApplicationUser>());

        _mockRoleManager
            .Setup(x => x.DeleteAsync(role))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _sut.DeleteRoleAsync(roleId);

        // Assert
        result.Should().BeTrue();

        _mockRoleManager.Verify(
            x => x.DeleteAsync(It.Is<ApplicationRole>(r => r.Id == roleId)),
            Times.Once);
    }

    [Fact]
    public async Task DeleteRoleAsync_WithUsersAssigned_ReturnsFalse()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var roleName = "TestRole";
        var role = new ApplicationRole { Id = roleId, Name = roleName };
        var usersInRole = new List<ApplicationUser> { MockData.CreateTestUser() };

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(role);

        _mockUserManager
            .Setup(x => x.GetUsersInRoleAsync(roleName))
            .ReturnsAsync(usersInRole);

        // Act
        var result = await _sut.DeleteRoleAsync(roleId);

        // Assert
        result.Should().BeFalse();

        _mockRoleManager.Verify(
            x => x.DeleteAsync(It.IsAny<ApplicationRole>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteRoleAsync_WithNonExistentRole_ReturnsFalse()
    {
        // Arrange
        var roleId = Guid.NewGuid();

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync((ApplicationRole?)null);

        // Act
        var result = await _sut.DeleteRoleAsync(roleId);

        // Assert
        result.Should().BeFalse();

        _mockRoleManager.Verify(
            x => x.DeleteAsync(It.IsAny<ApplicationRole>()),
            Times.Never);
    }

    [Fact]
    public async Task AssignRoleToUserAsync_WithValidData_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var roleName = "TestRole";
        var user = MockData.CreateTestUser();
        user.Id = userId;
        var role = new ApplicationRole { Id = roleId, Name = roleName };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(role);

        _mockUserManager
            .Setup(x => x.IsInRoleAsync(user, roleName))
            .ReturnsAsync(false);

        _mockUserManager
            .Setup(x => x.AddToRoleAsync(user, roleName))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _sut.AssignRoleToUserAsync(userId, roleId);

        // Assert
        result.Should().BeTrue();

        _mockUserManager.Verify(
            x => x.AddToRoleAsync(user, roleName),
            Times.Once);
    }

    [Fact]
    public async Task AssignRoleToUserAsync_WithNonExistentUser_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _sut.AssignRoleToUserAsync(userId, roleId);

        // Assert
        result.Should().BeFalse();

        _mockUserManager.Verify(
            x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task AssignRoleToUserAsync_WhenUserAlreadyHasRole_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var roleName = "TestRole";
        var user = MockData.CreateTestUser();
        var role = new ApplicationRole { Id = roleId, Name = roleName };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(role);

        _mockUserManager
            .Setup(x => x.IsInRoleAsync(user, roleName))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.AssignRoleToUserAsync(userId, roleId);

        // Assert
        result.Should().BeFalse();

        _mockUserManager.Verify(
            x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task RemoveRoleFromUserAsync_WithValidData_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var roleName = "TestRole";
        var user = MockData.CreateTestUser();
        var role = new ApplicationRole { Id = roleId, Name = roleName };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(role);

        _mockUserManager
            .Setup(x => x.IsInRoleAsync(user, roleName))
            .ReturnsAsync(true);

        _mockUserManager
            .Setup(x => x.RemoveFromRoleAsync(user, roleName))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _sut.RemoveRoleFromUserAsync(userId, roleId);

        // Assert
        result.Should().BeTrue();

        _mockUserManager.Verify(
            x => x.RemoveFromRoleAsync(user, roleName),
            Times.Once);
    }

    [Fact]
    public async Task RemoveRoleFromUserAsync_WhenUserDoesNotHaveRole_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var roleName = "TestRole";
        var user = MockData.CreateTestUser();
        var role = new ApplicationRole { Id = roleId, Name = roleName };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(roleId.ToString()))
            .ReturnsAsync(role);

        _mockUserManager
            .Setup(x => x.IsInRoleAsync(user, roleName))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.RemoveRoleFromUserAsync(userId, roleId);

        // Assert
        result.Should().BeFalse();

        _mockUserManager.Verify(
            x => x.RemoveFromRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task GetUserRolesAsync_WithValidUser_ReturnsRoles()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = MockData.CreateTestUser();
        var roleNames = new[] { "Admin", "Moderator" };
        var adminRole = new ApplicationRole { Id = Guid.NewGuid(), Name = "Admin" };
        var moderatorRole = new ApplicationRole { Id = Guid.NewGuid(), Name = "Moderator" };

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager
            .Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(roleNames.ToList());

        _mockRoleManager
            .Setup(x => x.FindByNameAsync("Admin"))
            .ReturnsAsync(adminRole);

        _mockRoleManager
            .Setup(x => x.FindByNameAsync("Moderator"))
            .ReturnsAsync(moderatorRole);

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(adminRole.Id.ToString()))
            .ReturnsAsync(adminRole);

        _mockRoleManager
            .Setup(x => x.FindByIdAsync(moderatorRole.Id.ToString()))
            .ReturnsAsync(moderatorRole);

        _mockRoleManager
            .Setup(x => x.GetClaimsAsync(It.IsAny<ApplicationRole>()))
            .ReturnsAsync(new List<Claim>());

        _mockUserManager
            .Setup(x => x.GetUsersInRoleAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<ApplicationUser>());

        // Act
        var result = await _sut.GetUserRolesAsync(userId);

        // Assert
        var roles = result.ToList();
        roles.Should().HaveCount(2);
        roles.Should().Contain(r => r.Name == "Admin");
        roles.Should().Contain(r => r.Name == "Moderator");
    }

    [Fact]
    public async Task GetUserRolesAsync_WithNonExistentUser_ReturnsEmptyList()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _mockUserManager
            .Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _sut.GetUserRolesAsync(userId);

        // Assert
        result.Should().BeEmpty();
    }
}
