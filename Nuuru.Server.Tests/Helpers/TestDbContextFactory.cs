using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Nuuru.Server.Data;
using Nuuru.Server.Models;
using Nuuru.Server.Models.Booru;

namespace Nuuru.Server.Tests.Helpers;

public class TestDbContextFactory
{
    public static ApplicationDbContext Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static ApplicationDbContext CreateWithSeedData()
    {
        var context = Create();
        SeedTestData(context);
        return context;
    }

    private static void SeedTestData(ApplicationDbContext context)
    {
        // Seed test users
        var testUser1 = new ApplicationUser
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            UserName = "testuser1",
            NormalizedUserName = "TESTUSER1",
            Email = "testuser1@example.com",
            NormalizedEmail = "TESTUSER1@EXAMPLE.COM",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            Status = "active",
            Biography = "Test user 1",
            DateCreated = DateTime.UtcNow.AddDays(-30)
        };

        var testUser2 = new ApplicationUser
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            UserName = "testuser2",
            NormalizedUserName = "TESTUSER2",
            Email = "testuser2@example.com",
            NormalizedEmail = "TESTUSER2@EXAMPLE.COM",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            Status = "active",
            Biography = "Test user 2",
            DateCreated = DateTime.UtcNow.AddDays(-20)
        };

        context.Users.AddRange(testUser1, testUser2);

        // Seed test tags
        var tag1 = new Tag
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "landscape",
            PostCount = 0
        };

        var tag2 = new Tag
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Name = "portrait",
            PostCount = 0
        };

        var tag3 = new Tag
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Name = "meta:duplicate",
            PostCount = 0
        };

        context.BooruTags.AddRange(tag1, tag2, tag3);

        // Seed test posts
        var post1 = new Post
        {
            Id = 1,
            StorageIdentifier = "storage/test1.jpg",
            ImageHash = "hash1234567890abcdef",
            MimeType = "image/jpeg",
            FileSize = 102400,
            OriginalFileName = "test1.jpg",
            Width = 1920,
            Height = 1080,
            UploadedAt = DateTime.UtcNow.AddDays(-10),
            Uploader = testUser1,
            PostTags = new List<PostTag>()
        };

        var post2 = new Post
        {
            Id = 2,
            StorageIdentifier = "storage/test2.png",
            ImageHash = "hash0987654321fedcba",
            MimeType = "image/png",
            FileSize = 204800,
            OriginalFileName = "test2.png",
            Width = 1280,
            Height = 720,
            UploadedAt = DateTime.UtcNow.AddDays(-5),
            Uploader = testUser2,
            PostTags = new List<PostTag>()
        };

        context.BooruPosts.AddRange(post1, post2);

        // Add post-tag relationships
        var postTag1 = new PostTag
        {
            PostId = post1.Id,
            TagId = tag1.Id,
            AddedAt = DateTime.UtcNow.AddDays(-10),
            IsLocked = false
        };

        var postTag2 = new PostTag
        {
            PostId = post2.Id,
            TagId = tag2.Id,
            AddedAt = DateTime.UtcNow.AddDays(-5),
            IsLocked = false
        };

        context.Set<PostTag>().AddRange(postTag1, postTag2);

        context.SaveChanges();

        // Update tag counts
        tag1.PostCount = 1;
        tag2.PostCount = 1;
        context.SaveChanges();
    }
}
