using FluentAssertions;
using Nuuru.Server.Auth;
using System.Security.Claims;

namespace Nuuru.Server.Tests.Unit.Auth;

public class PermissionCalculatorTests
{
    [Fact]
    public void ComputeEffectivePermissions_WithNoPermissions_ReturnsEmpty()
    {
        // Arrange
        var rolePermissions = Enumerable.Empty<string>();
        var userAllowPermissions = Enumerable.Empty<string>();
        var userDenyPermissions = Enumerable.Empty<string>();

        // Act
        var result = PermissionCalculator.ComputeEffectivePermissions(
            rolePermissions,
            userAllowPermissions,
            userDenyPermissions);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeEffectivePermissions_WithOnlyRolePermissions_ReturnsRolePermissions()
    {
        // Arrange
        var rolePermissions = new[] { "user.upload_post", "user.comment" };
        var userAllowPermissions = Enumerable.Empty<string>();
        var userDenyPermissions = Enumerable.Empty<string>();

        // Act
        var result = PermissionCalculator.ComputeEffectivePermissions(
            rolePermissions,
            userAllowPermissions,
            userDenyPermissions);

        // Assert
        result.Should().BeEquivalentTo(rolePermissions);
    }

    [Fact]
    public void ComputeEffectivePermissions_WithOnlyUserPermissions_ReturnsUserPermissions()
    {
        // Arrange
        var rolePermissions = Enumerable.Empty<string>();
        var userAllowPermissions = new[] { "user.upload_post", "user.comment" };
        var userDenyPermissions = Enumerable.Empty<string>();

        // Act
        var result = PermissionCalculator.ComputeEffectivePermissions(
            rolePermissions,
            userAllowPermissions,
            userDenyPermissions);

        // Assert
        result.Should().BeEquivalentTo(userAllowPermissions);
    }

    [Fact]
    public void ComputeEffectivePermissions_WithRoleAndUserPermissions_ReturnsUnion()
    {
        // Arrange
        var rolePermissions = new[] { "user.upload_post", "user.comment" };
        var userAllowPermissions = new[] { "user.edit_own_content" };
        var userDenyPermissions = Enumerable.Empty<string>();

        // Act
        var result = PermissionCalculator.ComputeEffectivePermissions(
            rolePermissions,
            userAllowPermissions,
            userDenyPermissions);

        // Assert
        result.Should().BeEquivalentTo(new[] { "user.upload_post", "user.comment", "user.edit_own_content" });
    }

    [Fact]
    public void ComputeEffectivePermissions_WithDenyPermissions_ExcludesDenied()
    {
        // Arrange
        var rolePermissions = new[] { "user.upload_post", "user.comment", "user.delete_own_content" };
        var userAllowPermissions = new[] { "user.edit_own_content" };
        var userDenyPermissions = new[] { "user.delete_own_content" };

        // Act
        var result = PermissionCalculator.ComputeEffectivePermissions(
            rolePermissions,
            userAllowPermissions,
            userDenyPermissions);

        // Assert
        var effective = result.ToList();
        effective.Should().HaveCount(3);
        effective.Should().Contain("user.upload_post");
        effective.Should().Contain("user.comment");
        effective.Should().Contain("user.edit_own_content");
        effective.Should().NotContain("user.delete_own_content"); // Denied!
    }

    [Fact]
    public void ComputeEffectivePermissions_WithUserDenyOverridingUserAllow_ExcludesDenied()
    {
        // Arrange
        var rolePermissions = Enumerable.Empty<string>();
        var userAllowPermissions = new[] { "user.delete_own_content" };
        var userDenyPermissions = new[] { "user.delete_own_content" };

        // Act
        var result = PermissionCalculator.ComputeEffectivePermissions(
            rolePermissions,
            userAllowPermissions,
            userDenyPermissions);

        // Assert
        result.Should().BeEmpty(); // Deny wins over allow
    }

    [Fact]
    public void ComputeEffectivePermissions_IsCaseInsensitive()
    {
        // Arrange
        var rolePermissions = new[] { "user.UPLOAD_POST" };
        var userAllowPermissions = new[] { "User.Comment" };
        var userDenyPermissions = new[] { "USER.DELETE_OWN_CONTENT" };

        // Act
        var result = PermissionCalculator.ComputeEffectivePermissions(
            rolePermissions,
            userAllowPermissions,
            userDenyPermissions);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public void ComputeEffectivePermissionsFromClaims_WithNoPermissions_ReturnsEmpty()
    {
        // Arrange
        var roleClaims = Enumerable.Empty<Claim>();
        var userClaims = Enumerable.Empty<Claim>();

        // Act
        var result = PermissionCalculator.ComputeEffectivePermissionsFromClaims(
            roleClaims,
            userClaims);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeEffectivePermissionsFromClaims_WithRoleAndUserClaims_ReturnsUnion()
    {
        // Arrange
        var roleClaims = new[]
        {
            new Claim(Permissions.ClaimType, "user.upload_post"),
            new Claim(Permissions.ClaimType, "user.comment")
        };
        var userClaims = new[]
        {
            new Claim(Permissions.ClaimType, "user.edit_own_content")
        };

        // Act
        var result = PermissionCalculator.ComputeEffectivePermissionsFromClaims(
            roleClaims,
            userClaims);

        // Assert
        result.Should().BeEquivalentTo(new[] { "user.upload_post", "user.comment", "user.edit_own_content" });
    }

    [Fact]
    public void ComputeEffectivePermissionsFromClaims_WithDenyClaims_ExcludesDenied()
    {
        // Arrange
        var roleClaims = new[]
        {
            new Claim(Permissions.ClaimType, "user.upload_post"),
            new Claim(Permissions.ClaimType, "user.comment"),
            new Claim(Permissions.ClaimType, "user.delete_own_content")
        };
        var userClaims = new[]
        {
            new Claim(Permissions.ClaimType, "user.edit_own_content"),
            new Claim(Permissions.DenyClaimType, "user.delete_own_content")
        };

        // Act
        var result = PermissionCalculator.ComputeEffectivePermissionsFromClaims(
            roleClaims,
            userClaims);

        // Assert
        var effective = result.ToList();
        effective.Should().HaveCount(3);
        effective.Should().Contain("user.upload_post");
        effective.Should().Contain("user.comment");
        effective.Should().Contain("user.edit_own_content");
        effective.Should().NotContain("user.delete_own_content"); // Denied!
    }

    [Fact]
    public void ComputeEffectivePermissionsFromClaims_IgnoresNonPermissionClaims()
    {
        // Arrange
        var roleClaims = new[]
        {
            new Claim(Permissions.ClaimType, "user.upload_post"),
            new Claim("some.other.type", "some.value")
        };
        var userClaims = new[]
        {
            new Claim(Permissions.ClaimType, "user.comment"),
            new Claim("email", "test@example.com")
        };

        // Act
        var result = PermissionCalculator.ComputeEffectivePermissionsFromClaims(
            roleClaims,
            userClaims);

        // Assert
        result.Should().BeEquivalentTo(new[] { "user.upload_post", "user.comment" });
    }

    [Fact]
    public void ComputeEffectivePermissions_WithDuplicates_ReturnsUniqueSet()
    {
        // Arrange
        var rolePermissions = new[] { "user.upload_post", "user.comment" };
        var userAllowPermissions = new[] { "user.upload_post", "user.edit_own_content" }; // Duplicate
        var userDenyPermissions = Enumerable.Empty<string>();

        // Act
        var result = PermissionCalculator.ComputeEffectivePermissions(
            rolePermissions,
            userAllowPermissions,
            userDenyPermissions);

        // Assert
        result.Should().BeEquivalentTo(new[] { "user.upload_post", "user.comment", "user.edit_own_content" });
        result.Count().Should().Be(3); // No duplicates
    }
}
