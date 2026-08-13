using Nuuru.Server.Models;
using Nuuru.Server.Models.Booru;

namespace Nuuru.Server.Tests.Helpers;

public static class MockData
{
    public static ApplicationUser CreateTestUser(
        string userName = "testuser",
        string email = "test@example.com",
        Guid? userId = null)
    {
        return new ApplicationUser
        {
            Id = userId ?? Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            Status = "active",
            Biography = $"Biography for {userName}",
            DateCreated = DateTime.UtcNow
        };
    }

    public static Post CreateTestPost(
        ApplicationUser uploader,
        int postId = 1,
        string hash = "testhash123",
        string mimeType = "image/jpeg",
        bool isApproved = true)
    {
        return new Post
        {
            Id = postId,
            StorageIdentifier = $"storage/post_{postId}.jpg",
            ImageHash = hash,
            MimeType = mimeType,
            FileSize = 102400,
            OriginalFileName = $"test_image_{postId}.jpg",
            Width = 1920,
            Height = 1080,
            UploadedAt = DateTime.UtcNow,
            Uploader = uploader,
            UploaderId = uploader.Id,
            PostTags = new List<PostTag>(),
            Comments = new List<Comment>(),
            IsApproved = isApproved
        };
    }

    public static TagCategory CreateTestTagCategory(
        string? slug = null,
        string? name = null,
        Guid? categoryId = null)
    {
        var id = categoryId ?? Guid.NewGuid();
        var actualSlug = slug ?? $"category-{id:N}";
        return new TagCategory
        {
            Id = id,
            Name = name ?? actualSlug.ToUpperInvariant(),
            Slug = actualSlug,
            ColorHex = "#90a4ae",
            SortOrder = 0,
            IsActive = true
        };
    }

    public static Tag CreateTestTag(
        string name = "test-tag",
        Guid? tagId = null,
        int postCount = 0,
        TagCategory? category = null)
    {
        return new Tag
        {
            Id = tagId ?? Guid.NewGuid(),
            Name = name,
            Category = category ?? CreateTestTagCategory(),
            PostCount = postCount
        };
    }

    public static PostTag CreatePostTag(int postId, Guid tagId)
    {
        return new PostTag
        {
            PostId = postId,
            TagId = tagId,
            AddedAt = DateTime.UtcNow,
            IsLocked = false
        };
    }

    public static Comment CreateTestComment(
        int postId,
        ApplicationUser user,
        string content = "Test comment")
    {
        return new Comment
        {
            PostId = postId,
            UserId = user.Id,
            User = user,
            ContentRaw = content,
            ContentHtml = content,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static Ban CreateTestBan(
        Guid userId,
        BanZone zone = BanZone.Sitewide,
        DateTime? endTime = null,
        string reason = "Test ban")
    {
        // Create a user for the ban
        var user = CreateTestUser($"user_{userId:N}", $"user_{userId:N}@example.com", userId);

        return new Ban
        {
            Id = Guid.NewGuid(),
            User = user,
            Zone = zone,
            Reason = reason,
            StartTime = DateTime.UtcNow,
            EndTime = endTime ?? DateTime.UtcNow.AddDays(7),
            Active = true
        };
    }

    public static MemoryStream CreateTestImageStream(int sizeInBytes = 1024)
    {
        var bytes = new byte[sizeInBytes];
        // Fill with some pattern to simulate image data
        for (int i = 0; i < sizeInBytes; i++)
        {
            bytes[i] = (byte)(i % 256);
        }
        return new MemoryStream(bytes);
    }
}
