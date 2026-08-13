using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nuuru.Server.Data;
using Nuuru.Server.Auth;
using Nuuru.Server.Models;
using Nuuru.Server.Models.Booru;
using Nuuru.Server.Models.Messaging;
using Nuuru.Server.Services;
using Nuuru.Server.Services.BBCode;
using Nuuru.Server.Services.Storage;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Nuuru.Tools.ShimmieMigration.Source;
using FFMpegCore;
using SixLabors.ImageSharp;
using Spectre.Console;

namespace Nuuru.Tools.ShimmieMigration;

public class MigrationService
{
    private readonly IShimmieDataSource _shimmie;
    private readonly ApplicationDbContext _nuuru;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IFileStorageService _fileStorageService;
    private readonly MigrationOptions _options;

    // Mapping dictionaries to track migrated entities
    private readonly Dictionary<int, Guid> _userIdMap = new();
    private readonly Dictionary<string, Guid> _usernameToGuidMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> _bannedUsers = new(); // userId -> shimmie class
    private readonly Dictionary<int, Guid> _tagIdMap = new();
    private readonly Dictionary<string, Guid> _tagCategoryMap = new();
    private readonly BBCodeService _bbCodeService = new();

    private static readonly Regex MentionRegex = new(
        @"\[url=site://post/view/(\d+)(?:#c(\d+))?\]@([^\[]+)\[/url\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AlignRegex = new(
        @"\[align=[^\]]*\](.*?)\[/align\]",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public MigrationService(
        IShimmieDataSource shimmie,
        ApplicationDbContext nuuru,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IFileStorageService fileStorageService,
        MigrationOptions options)
    {
        _shimmie = shimmie;
        _nuuru = nuuru;
        _userManager = userManager;
        _roleManager = roleManager;
        _fileStorageService = fileStorageService;
        _options = options;
    }

    public async Task RunMigrationAsync(CancellationToken ct = default)
    {
        AnsiConsole.MarkupLine("[bold blue]Starting Shimmie to Nuuru Migration[/]");
        AnsiConsole.WriteLine();

        // Ensure database schema exists
        AnsiConsole.MarkupLine("[yellow]Initializing target database schema...[/]");
        await _nuuru.Database.MigrateAsync(ct);
        AnsiConsole.MarkupLine("[green]Database schema ready[/]");

        await MigrateRolesAsync(ct);
        await MigrateUsersAsync(ct);
        await MigrateBansAsync(ct);
        await FetchGravatarAvatarsAsync(ct);
        await MigrateTagCategoriesAsync(ct);
        await MigrateTagsAsync(ct);
        await MigrateTagAliasesAsync(ct);
        await MigrateTagImplicationsAsync(ct);
        await MigratePostsAsync(ct);
        await MigratePostTagsAsync(ct);
        await MigrateCommentsAsync(ct);
        await MigrateFavoritesAsync(ct);
        await MigrateVotesAsync(ct);
        await MigratePrivateMessagesAsync(ct);
        await MigrateTagHistoriesAsync(ct);
        await MigrateSourceHistoriesAsync(ct);
        await UpdateTagCountsAsync(ct);
        await ResetPostgresSequencesAsync(ct);

        AnsiConsole.MarkupLine("[bold green]Migration completed successfully![/]");
    }

    public async Task RunTagSyncAsync(CancellationToken ct = default)
    {
        AnsiConsole.MarkupLine("[bold blue]Starting Tag Sync from Shimmie[/]");
        AnsiConsole.WriteLine();

        // Step 1: Build user ID map from existing users (batch load)
        AnsiConsole.MarkupLine("[yellow]Building user mapping...[/]");
        var shimmieUsers = await _shimmie.GetUsersAsync(ct);
        var nuuruUsersByName = await _nuuru.Users
            .ToDictionaryAsync(u => u.UserName!, u => u.Id, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var shimmieUser in shimmieUsers)
        {
            if (nuuruUsersByName.TryGetValue(shimmieUser.Name, out var nuuruUserId))
                _userIdMap[shimmieUser.Id] = nuuruUserId;
        }
        AnsiConsole.MarkupLine($"[green]Mapped {_userIdMap.Count} users[/]");

        // Step 2: Build tag ID map — match Shimmie tags to Nuuru tags by name + category
        AnsiConsole.MarkupLine("[yellow]Building tag mapping...[/]");

        // Pre-load all Nuuru categories in one query
        var nuuruCategories = await _nuuru.BooruTagCategories.ToListAsync(ct);
        var nuuruCategoriesBySlug = nuuruCategories.ToDictionary(c => c.Slug, c => c.Id, StringComparer.OrdinalIgnoreCase);

        var shimmieCategories = await _shimmie.GetTagCategoriesAsync(ct);
        var sortOrder = nuuruCategories.Count > 0 ? nuuruCategories.Max(c => c.SortOrder) + 1 : 0;
        foreach (var cat in shimmieCategories)
        {
            if (nuuruCategoriesBySlug.TryGetValue(cat.Category.ToLowerInvariant(), out var catId))
            {
                _tagCategoryMap[cat.Category] = catId;
            }
            else
            {
                var newCategory = new TagCategory
                {
                    Id = Guid.NewGuid(),
                    Name = cat.DisplaySingular ?? cat.Category,
                    Slug = cat.Category.ToLowerInvariant(),
                    ColorHex = cat.Color,
                    SortOrder = sortOrder++,
                    IsActive = true
                };
                _nuuru.BooruTagCategories.Add(newCategory);
                _tagCategoryMap[cat.Category] = newCategory.Id;
                nuuruCategoriesBySlug[newCategory.Slug] = newCategory.Id;
            }
        }
        await _nuuru.SaveChangesAsync(ct);

        var shimmieTags = await _shimmie.GetTagsAsync(ct);
        // Pre-load all Nuuru tags with categories for efficient matching
        var nuuruTags = await _nuuru.BooruTags
            .Include(t => t.Category)
            .ToListAsync(ct);
        var nuuruTagLookup = nuuruTags
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var newTagBatch = new List<Tag>();
        foreach (var shimmieTag in shimmieTags)
        {
            var (categorySlug, tagName) = ParseTagWithCategory(shimmieTag.Tag);
            Guid? expectedCategoryId = null;
            if (categorySlug != null && _tagCategoryMap.TryGetValue(categorySlug, out var catId))
                expectedCategoryId = catId;

            Tag? match = null;
            if (nuuruTagLookup.TryGetValue(tagName, out var candidates))
            {
                // Match on name + category (exact match only)
                match = candidates
                    .FirstOrDefault(t =>
                        (expectedCategoryId == null && t.Category == null) ||
                        (expectedCategoryId != null && t.Category?.Id == expectedCategoryId));
            }

            if (match != null)
            {
                _tagIdMap[shimmieTag.Id] = match.Id;
            }
            else
            {
                var newTag = new Tag
                {
                    Id = Guid.NewGuid(),
                    Name = tagName,
                    PostCount = 0,
                    CreatedAt = DateTime.UtcNow
                };

                if (expectedCategoryId.HasValue)
                    newTag.Category = await _nuuru.BooruTagCategories.FindAsync([expectedCategoryId.Value], ct);

                newTagBatch.Add(newTag);
                _tagIdMap[shimmieTag.Id] = newTag.Id;

                // Add to lookup so subsequent duplicates match
                if (!nuuruTagLookup.ContainsKey(tagName))
                    nuuruTagLookup[tagName] = new List<Tag>();
                nuuruTagLookup[tagName].Add(newTag);

                if (newTagBatch.Count >= _options.BatchSize)
                {
                    _nuuru.BooruTags.AddRange(newTagBatch);
                    await _nuuru.SaveChangesAsync(ct);
                    newTagBatch.Clear();
                }
            }
        }

        if (newTagBatch.Count > 0)
        {
            _nuuru.BooruTags.AddRange(newTagBatch);
            await _nuuru.SaveChangesAsync(ct);
        }

        // Validate _tagIdMap against actual DB to prune any phantom entries
        var existingTagIds = await _nuuru.BooruTags.Select(t => t.Id).ToHashSetAsync(ct);
        var phantomCount = 0;
        foreach (var kvp in _tagIdMap.Where(kvp => !existingTagIds.Contains(kvp.Value)).ToList())
        {
            _tagIdMap.Remove(kvp.Key);
            phantomCount++;
        }
        if (phantomCount > 0)
            AnsiConsole.MarkupLine($"[yellow]Pruned {phantomCount} tag mappings with no matching DB row[/]");

        AnsiConsole.MarkupLine($"[green]Mapped/created {_tagIdMap.Count}/{shimmieTags.Count} tags[/]");

        // Step 3: Get latest tag history dates from both databases
        AnsiConsole.MarkupLine("[yellow]Comparing tag history dates...[/]");

        // Fetch Shimmie tag histories
        var shimmieTagHistories = await _shimmie.GetTagHistoriesAsync(ct);

        // Group Shimmie tag histories by post
        var shimmieTagHistoriesByPost = shimmieTagHistories
            .GroupBy(h => h.ImageId)
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.Id).ToList());

        // Determine upper bound: highest Shimmie post ID across tag histories and image tags
        var maxShimmiePostId = shimmieTagHistoriesByPost.Count > 0
            ? shimmieTagHistoriesByPost.Keys.Max()
            : 0;
        AnsiConsole.MarkupLine($"[dim]Highest Shimmie post ID with tag history: {maxShimmiePostId}[/]");

        var existingPostIds = maxShimmiePostId > 0
            ? await _nuuru.BooruPosts
                .Where(p => p.Id <= maxShimmiePostId)
                .Select(p => p.Id)
                .ToHashSetAsync(ct)
            : new HashSet<int>();

        var nuuruLatestDates = await _nuuru.TagHistories
            .Where(h => h.PostId <= maxShimmiePostId)
            .GroupBy(h => h.PostId)
            .Select(g => new { PostId = g.Key, LatestDate = g.Max(h => h.DateSet) })
            .ToDictionaryAsync(x => x.PostId, x => x.LatestDate, ct);

        // Determine which posts to sync (history-based eligibility)
        var postsToSync = new HashSet<int>();
        foreach (var (postId, shimmieHistories) in shimmieTagHistoriesByPost)
        {
            // Post must exist in Nuuru
            if (!existingPostIds.Contains(postId))
                continue;

            var shimmieLatest = shimmieHistories.Max(h => DateTime.SpecifyKind(h.DateSet, DateTimeKind.Utc));

            if (!nuuruLatestDates.TryGetValue(postId, out var nuuruLatest))
            {
                // No tag history in Nuuru — safe to sync
                postsToSync.Add(postId);
            }
            else if (shimmieLatest >= nuuruLatest)
            {
                // Shimmie has same or newer data — Nuuru hasn't been modified since migration
                postsToSync.Add(postId);
            }
        }

        AnsiConsole.MarkupLine($"[cyan]{postsToSync.Count} posts eligible for tag sync (out of {shimmieTagHistoriesByPost.Count} with history in source)[/]");

        // Also include posts that have tags in Shimmie but no tag history at all —
        // these are older posts that predate Shimmie's tag history tracking
        var shimmiePostTags = await _shimmie.GetImageTagsAsync(ct);
        var shimmiePostTagsByPost = shimmiePostTags
            .GroupBy(pt => pt.ImageId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Expand upper bound and existing post set to include historyless posts
        var maxShimmieImageId = shimmiePostTagsByPost.Count > 0
            ? Math.Max(maxShimmiePostId, shimmiePostTagsByPost.Keys.Max())
            : maxShimmiePostId;
        if (maxShimmieImageId > maxShimmiePostId)
        {
            var additionalPostIds = await _nuuru.BooruPosts
                .Where(p => p.Id > maxShimmiePostId && p.Id <= maxShimmieImageId)
                .Select(p => p.Id)
                .ToListAsync(ct);
            foreach (var id in additionalPostIds)
                existingPostIds.Add(id);

            // Expand nuuruLatestDates to cover the wider range so we can detect
            // posts that were tagged in Nuuru (and should not be overwritten)
            var additionalDates = await _nuuru.TagHistories
                .Where(h => h.PostId > maxShimmiePostId && h.PostId <= maxShimmieImageId)
                .GroupBy(h => h.PostId)
                .Select(g => new { PostId = g.Key, LatestDate = g.Max(h => h.DateSet) })
                .ToListAsync(ct);
            foreach (var x in additionalDates)
                nuuruLatestDates[x.PostId] = x.LatestDate;
        }

        var historylessCount = 0;
        foreach (var postId in shimmiePostTagsByPost.Keys)
        {
            if (!existingPostIds.Contains(postId))
                continue;
            if (postsToSync.Contains(postId))
                continue;
            if (shimmieTagHistoriesByPost.ContainsKey(postId))
                continue; // Has history but was excluded (Nuuru was modified) — respect that
            if (nuuruLatestDates.ContainsKey(postId))
                continue; // Has Nuuru-side history — someone tagged it in Nuuru, don't overwrite

            postsToSync.Add(postId);
            historylessCount++;
        }

        if (historylessCount > 0)
            AnsiConsole.MarkupLine($"[cyan]{historylessCount} additional posts with tags but no history in source[/]");

        AnsiConsole.MarkupLine($"[cyan]Total posts to sync: {postsToSync.Count}[/]");

        if (postsToSync.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]All posts have been modified in Nuuru — nothing to sync[/]");
            return;
        }

        // Fetch remaining Shimmie data only after confirming there's work to do
        var shimmieSourceHistories = await _shimmie.GetSourceHistoriesAsync(ct);
        var shimmieSourceHistoriesByPost = shimmieSourceHistories
            .GroupBy(h => h.ImageId)
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.Id).ToList());

        // Step 4: Bulk delete existing data for posts to sync (chunked to avoid oversized IN clauses)
        AnsiConsole.MarkupLine("[yellow]Clearing existing tag data for eligible posts...[/]");

        foreach (var chunk in postsToSync.Chunk(_options.BatchSize))
        {
            var chunkSet = chunk.ToHashSet();

            await _nuuru.TagHistories
                .Where(h => chunkSet.Contains(h.PostId))
                .ExecuteDeleteAsync(ct);

            await _nuuru.SourceHistories
                .Where(h => chunkSet.Contains(h.PostId))
                .ExecuteDeleteAsync(ct);

            await _nuuru.Set<PostTag>()
                .Where(pt => chunkSet.Contains(pt.PostId))
                .ExecuteDeleteAsync(ct);
        }

        // Step 5: Insert synced data
        // Pre-load surviving PostTag keys to avoid PK conflicts from partial prior runs
        var existingPostTags = await _nuuru.Set<PostTag>()
            .Where(pt => postsToSync.Contains(pt.PostId))
            .Select(pt => new { pt.PostId, pt.TagId })
            .ToListAsync(ct);
        var existingPostTagKeys = existingPostTags.Select(x => (x.PostId, x.TagId)).ToHashSet();
        if (existingPostTagKeys.Count > 0)
            AnsiConsole.MarkupLine($"[yellow]{existingPostTagKeys.Count} PostTag rows survived delete — will skip duplicates[/]");

        var syncedCount = 0;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Syncing tags[/]", maxValue: postsToSync.Count);

                foreach (var postId in postsToSync)
                {
                    // Insert Shimmie tag histories
                    if (shimmieTagHistoriesByPost.TryGetValue(postId, out var tagHistories))
                        InsertTagHistoriesForPost(tagHistories);

                    // Insert Shimmie source histories
                    if (shimmieSourceHistoriesByPost.TryGetValue(postId, out var sourceHistories))
                        InsertSourceHistoriesForPost(sourceHistories);

                    // Insert Shimmie post-tag associations (deduplicate by composite key)
                    if (shimmiePostTagsByPost.TryGetValue(postId, out var postTagEntries))
                    {
                        var now = DateTime.UtcNow;
                        var seenPostTags = new HashSet<(int PostId, Guid TagId)>();
                        foreach (var pt in postTagEntries)
                        {
                            if (!_tagIdMap.TryGetValue(pt.TagId, out var tagId))
                                continue;

                            if (!seenPostTags.Add((pt.ImageId, tagId)))
                                continue;

                            if (existingPostTagKeys.Contains((pt.ImageId, tagId)))
                                continue;

                            _nuuru.Set<PostTag>().Add(new PostTag
                            {
                                PostId = pt.ImageId,
                                TagId = tagId,
                                AddedAt = now
                            });
                        }
                    }

                    syncedCount++;

                    // Save in batches and clear change tracker
                    if (syncedCount % _options.BatchSize == 0)
                    {
                        await _nuuru.SaveChangesAsync(ct);
                        _nuuru.ChangeTracker.Clear();
                    }

                    task.Increment(1);
                }

                // Final save
                await _nuuru.SaveChangesAsync(ct);
            });

        AnsiConsole.MarkupLine($"[green]Tag sync complete: {syncedCount} posts synced[/]");

        // Step 6: Update tag counts
        await UpdateTagCountsAsync(ct);

        AnsiConsole.MarkupLine("[bold green]Tag sync completed successfully![/]");
    }

    /// <summary>
    /// Inserts tag history entries for a single post, enforcing monotonically increasing dates.
    /// </summary>
    private void InsertTagHistoriesForPost(List<ShimmieTagHistory> histories)
    {
        var lastDate = DateTime.MinValue;
        foreach (var history in histories)
        {
            if (!_userIdMap.TryGetValue(history.UserId, out var userId))
                continue;

            var dateSet = DateTime.SpecifyKind(history.DateSet, DateTimeKind.Utc);
            if (dateSet <= lastDate)
                dateSet = lastDate.AddSeconds(1);
            lastDate = dateSet;

            _nuuru.TagHistories.Add(new TagHistory
            {
                PostId = history.ImageId,
                UserId = userId,
                UserIp = history.UserIp,
                Tags = history.Tags,
                DateSet = dateSet
            });
        }
    }

    /// <summary>
    /// Inserts source history entries for a single post, enforcing monotonically increasing dates.
    /// </summary>
    private void InsertSourceHistoriesForPost(List<ShimmieSourceHistory> histories)
    {
        var lastDate = DateTime.MinValue;
        foreach (var history in histories)
        {
            if (!_userIdMap.TryGetValue(history.UserId, out var userId))
                continue;

            var dateSet = DateTime.SpecifyKind(history.DateSet, DateTimeKind.Utc);
            if (dateSet <= lastDate)
                dateSet = lastDate.AddSeconds(1);
            lastDate = dateSet;

            _nuuru.SourceHistories.Add(new SourceHistory
            {
                PostId = history.ImageId,
                UserId = userId,
                UserIp = history.UserIp,
                Source = history.Source,
                DateSet = dateSet
            });
        }
    }

    private async Task MigrateRolesAsync(CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[yellow]Migrating roles...[/]");

        var shimmieClasses = await _shimmie.GetDistinctUserClassesAsync(ct);

        foreach (var className in shimmieClasses)
        {
            var roleName = MapShimmieClassToRole(className);
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                var role = new ApplicationRole { Name = roleName };
                await _roleManager.CreateAsync(role);
                AnsiConsole.MarkupLine($"  Created role: [cyan]{roleName}[/]");

                // Add permission claims based on role
                var permissions = GetPermissionsForRole(roleName);
                foreach (var permission in permissions)
                {
                    await _roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
                }
                if (permissions.Length > 0)
                {
                    AnsiConsole.MarkupLine($"    Added {permissions.Length} permissions");
                }
            }
        }

        AnsiConsole.MarkupLine($"[green]Roles migration complete[/]");
    }

    private static string[] GetPermissionsForRole(string roleName)
    {
        return roleName switch
        {
            "Admin" =>
            [
                // All admin permissions
                Permissions.Admin.ManageUsers,
                Permissions.Admin.ManagePermissions,
                Permissions.Admin.SystemSettings,
                Permissions.Admin.SendAnnouncements,
                Permissions.Admin.DeletePost,
                Permissions.Admin.ViewTrash,
                // All moderation permissions
                Permissions.Moderation.TrashPost,
                Permissions.Moderation.DeleteComment,
                Permissions.Moderation.EditTags,
                Permissions.Moderation.ApprovePost,
                Permissions.Moderation.BanUser,
                Permissions.Moderation.ReviewBanAppeals,
                Permissions.Moderation.ViewReports,
                Permissions.Moderation.ViewAuditLog,
                Permissions.Moderation.ViewIps,
                Permissions.Moderation.SuppressHistory,
                Permissions.Moderation.LockComments,
                Permissions.Moderation.FeaturePost,
                Permissions.Moderation.SetRating,
                Permissions.Moderation.SetSource,
                // All forum moderation
                Permissions.Forum.PinThread,
                Permissions.Forum.LockThread,
                Permissions.Forum.DeletePost,
                Permissions.Forum.DeleteThread,
                Permissions.Forum.MoveThread,
                // All user permissions
                Permissions.User.UploadPost,
                Permissions.User.AutoApprove,
                Permissions.User.Comment,
                Permissions.User.EditOwnContent,
                Permissions.User.DeleteOwnContent,
                Permissions.User.EditTags,
                Permissions.User.SetRating,
                Permissions.User.SetSource,
                Permissions.User.Vote,
                Permissions.User.Favorite,
                Permissions.User.CreateReport,
                Permissions.User.React,
                Permissions.Forum.CreateThread,
                Permissions.Forum.Reply,
                Permissions.Forum.UploadAttachment,
                Permissions.Messaging.SendMessage,
                Permissions.Messaging.CreateGroupConversation,
            ],
            "Moderator" =>
            [
                // Moderation permissions
                Permissions.Moderation.TrashPost,
                Permissions.Moderation.DeleteComment,
                Permissions.Moderation.EditTags,
                Permissions.Moderation.ApprovePost,
                Permissions.Moderation.BanUser,
                Permissions.Moderation.ViewReports,
                Permissions.Moderation.ViewAuditLog,
                Permissions.Moderation.SuppressHistory,
                Permissions.Moderation.SetRating,
                Permissions.Moderation.SetSource,
                // User permissions
                Permissions.User.UploadPost,
                Permissions.User.AutoApprove,
                Permissions.User.Comment,
                Permissions.User.EditOwnContent,
                Permissions.User.DeleteOwnContent,
                Permissions.User.EditTags,
                Permissions.User.SetRating,
                Permissions.User.SetSource,
                Permissions.User.Vote,
                Permissions.User.Favorite,
                Permissions.User.CreateReport,
                Permissions.User.React,
                Permissions.Forum.CreateThread,
                Permissions.Forum.Reply,
                Permissions.Forum.UploadAttachment,
                Permissions.Messaging.SendMessage,
                Permissions.Messaging.CreateGroupConversation,
            ],
            "Janitor" =>
            [
                // Moderation permissions
                Permissions.Moderation.ApprovePost,
                Permissions.Moderation.TrashPost,
                Permissions.Moderation.DeleteComment,
                // User permissions
                Permissions.User.UploadPost,
                Permissions.User.AutoApprove,
                Permissions.User.Comment,
                Permissions.User.EditOwnContent,
                Permissions.User.DeleteOwnContent,
                Permissions.User.EditTags,
                Permissions.User.SetRating,
                Permissions.User.SetSource,
                Permissions.User.Vote,
                Permissions.User.Favorite,
                Permissions.User.CreateReport,
                Permissions.User.React,
                Permissions.Forum.CreateThread,
                Permissions.Forum.Reply,
                Permissions.Forum.UploadAttachment,
                Permissions.Messaging.SendMessage,
                Permissions.Messaging.CreateGroupConversation,
            ],
            "Approver" =>
            [
                // Moderation permissions
                Permissions.Moderation.ApprovePost,
                // User permissions
                Permissions.User.UploadPost,
                Permissions.User.AutoApprove,
                Permissions.User.Comment,
                Permissions.User.EditOwnContent,
                Permissions.User.DeleteOwnContent,
                Permissions.User.EditTags,
                Permissions.User.SetRating,
                Permissions.User.SetSource,
                Permissions.User.Vote,
                Permissions.User.Favorite,
                Permissions.User.CreateReport,
                Permissions.User.React,
                Permissions.Forum.CreateThread,
                Permissions.Forum.Reply,
                Permissions.Forum.UploadAttachment,
                Permissions.Messaging.SendMessage,
                Permissions.Messaging.CreateGroupConversation,
            ],
            "Trusted" =>
            [
                // User permissions
                Permissions.User.UploadPost,
                Permissions.User.AutoApprove,
                Permissions.User.Comment,
                Permissions.User.EditOwnContent,
                Permissions.User.DeleteOwnContent,
                Permissions.User.EditTags,
                Permissions.User.SetRating,
                Permissions.User.SetSource,
                Permissions.User.Vote,
                Permissions.User.Favorite,
                Permissions.User.CreateReport,
                Permissions.User.React,
                Permissions.Forum.CreateThread,
                Permissions.Forum.Reply,
                Permissions.Forum.UploadAttachment,
                Permissions.Messaging.SendMessage,
                Permissions.Messaging.CreateGroupConversation,
            ],
            "User" =>
            [
                Permissions.User.UploadPost,
                Permissions.User.Comment,
                Permissions.User.EditOwnContent,
                Permissions.User.DeleteOwnContent,
                Permissions.User.EditTags,
                Permissions.User.SetRating,
                Permissions.User.SetSource,
                Permissions.User.Vote,
                Permissions.User.Favorite,
                Permissions.User.CreateReport,
                Permissions.User.React,
                Permissions.Forum.CreateThread,
                Permissions.Forum.Reply,
                Permissions.Forum.UploadAttachment,
                Permissions.Messaging.SendMessage,
                Permissions.Messaging.CreateGroupConversation,
            ],
            _ => []
        };
    }

    private async Task MigrateUsersAsync(CancellationToken ct)
    {
        var users = await _shimmie.GetUsersAsync(ct);
        var totalUsers = users.Count;
        AnsiConsole.MarkupLine($"[yellow]Migrating {totalUsers} users...[/]");

        var isFirstUser = true;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Migrating users[/]", maxValue: totalUsers);

                foreach (var shimmieUser in users)
                {
                    var existingUser = await _userManager.FindByNameAsync(shimmieUser.Name);
                    if (existingUser != null)
                    {
                        _userIdMap[shimmieUser.Id] = existingUser.Id;
                        _usernameToGuidMap[shimmieUser.Name] = existingUser.Id;

                        // Mark first user (lowest Shimmie ID) as system account
                        if (isFirstUser)
                        {
                            existingUser.IsSystemAccount = true;
                            await _nuuru.SaveChangesAsync(ct);
                            isFirstUser = false;
                        }

                        // Track banned users for ban record creation
                        if (IsBannedClass(shimmieUser.Class))
                        {
                            _bannedUsers[existingUser.Id] = shimmieUser.Class;
                        }

                        task.Increment(1);
                        continue;
                    }

                    var newUser = new ApplicationUser
                    {
                        Id = Guid.NewGuid(),
                        UserName = shimmieUser.Name,
                        NormalizedUserName = shimmieUser.Name.ToUpperInvariant(),
                        Email = shimmieUser.Email,
                        NormalizedEmail = shimmieUser.Email?.ToUpperInvariant(),
                        EmailConfirmed = true,
                        DateCreated = DateTime.SpecifyKind(shimmieUser.JoinDate, DateTimeKind.Utc),
                        Status = string.Empty,
                        Biography = string.Empty,
                        SecurityStamp = Guid.NewGuid().ToString(),
                        IsSystemAccount = isFirstUser
                    };
                    if (isFirstUser) isFirstUser = false;

                    var result = await _userManager.CreateAsync(newUser);
                    if (result.Succeeded)
                    {
                        _userIdMap[shimmieUser.Id] = newUser.Id;
                        _usernameToGuidMap[shimmieUser.Name] = newUser.Id;

                        // Copy Shimmie password hash for transparent migration
                        if (!string.IsNullOrEmpty(shimmieUser.Pass))
                        {
                            newUser.PasswordHash = shimmieUser.Pass;
                            await _nuuru.SaveChangesAsync(ct);
                        }

                        var roleName = MapShimmieClassToRole(shimmieUser.Class);
                        await _userManager.AddToRoleAsync(newUser, roleName);

                        // Track banned users for ban record creation
                        if (IsBannedClass(shimmieUser.Class))
                        {
                            _bannedUsers[newUser.Id] = shimmieUser.Class;
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Failed to create user {shimmieUser.Name}: {string.Join(", ", result.Errors.Select(e => e.Description))}[/]");
                    }

                    task.Increment(1);
                }
            });

        AnsiConsole.MarkupLine($"[green]Users migration complete: {_userIdMap.Count} users migrated[/]");
    }

    private async Task MigrateBansAsync(CancellationToken ct)
    {
        if (_bannedUsers.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No banned users to migrate[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[yellow]Migrating {_bannedUsers.Count} user bans...[/]");

        var existingBanUserIds = await _nuuru.Bans
            .Select(b => b.User.Id)
            .ToHashSetAsync(ct);

        var usersById = await _nuuru.Users
            .Where(u => _bannedUsers.Keys.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var migratedCount = 0;
        var skippedCount = 0;

        foreach (var (userId, shimmieClass) in _bannedUsers)
        {
            if (existingBanUserIds.Contains(userId))
            {
                skippedCount++;
                continue;
            }

            if (!usersById.TryGetValue(userId, out var user))
            {
                skippedCount++;
                continue;
            }

            var reason = shimmieClass.ToLowerInvariant() switch
            {
                "hellbanned" => "Migrated from Shimmie (shadowbanned)",
                "blocked" => "Migrated from Shimmie (blocked)",
                "ghost" => "Migrated from Shimmie (ghost)",
                _ => $"Migrated from Shimmie ({shimmieClass})"
            };

            var ban = new Ban
            {
                Id = Guid.NewGuid(),
                User = user,
                Reason = reason,
                StartTime = user.DateCreated,
                EndTime = DateTime.UtcNow.AddYears(100),
                Zone = BanZone.Sitewide,
                Active = true
            };

            _nuuru.Bans.Add(ban);
            migratedCount++;
        }

        await _nuuru.SaveChangesAsync(ct);
        AnsiConsole.MarkupLine($"[green]Bans migration complete: {migratedCount} migrated, {skippedCount} skipped[/]");
    }

    private async Task FetchGravatarAvatarsAsync(CancellationToken ct)
    {
        if (!_options.FetchGravatarAvatars)
        {
            AnsiConsole.MarkupLine("[dim]Skipping Gravatar avatars (disabled)[/]");
            return;
        }

        // Get non-banned users with emails
        var usersWithEmails = await _nuuru.Users
            .Where(u => u.Email != null && u.Email != "")
            .Where(u => !_nuuru.Bans.Any(b => b.User.Id == u.Id && b.Active && b.EndTime > DateTime.UtcNow))
            .Where(u => u.AvatarStorageIdentifier == null)
            .ToListAsync(ct);

        if (usersWithEmails.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No users eligible for Gravatar avatars[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[yellow]Fetching Gravatar avatars for {usersWithEmails.Count} users...[/]");

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        var fetchedCount = 0;
        var skippedCount = 0;
        var storedFileBatch = new List<StoredFile>();

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Fetching avatars[/]", maxValue: usersWithEmails.Count);

                foreach (var user in usersWithEmails)
                {
                    try
                    {
                        var emailHash = GetMd5Hash(user.Email!.Trim().ToLowerInvariant());
                        var gravatarUrl = $"https://www.gravatar.com/avatar/{emailHash}?s=256&d=404";

                        var response = await httpClient.GetAsync(gravatarUrl, ct);

                        if (response.IsSuccessStatusCode)
                        {
                            var imageBytes = await response.Content.ReadAsByteArrayAsync(ct);

                            // Generate GUID-based file identifier (matches LocalFileStorageService format)
                            var fileIdentifier = Guid.NewGuid().ToString("N");
                            var subDir = fileIdentifier.Substring(0, 2);
                            var fullPath = Path.Combine(_options.NuuruUploadsPath, subDir, fileIdentifier);

                            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                            await File.WriteAllBytesAsync(fullPath, imageBytes, ct);

                            // Calculate SHA256 hash
                            var hash = Convert.ToHexStringLower(
                                System.Security.Cryptography.SHA256.HashData(imageBytes));

                            // Create StoredFile record (required by IFileStorageService)
                            storedFileBatch.Add(new StoredFile
                            {
                                Id = Guid.NewGuid(),
                                FileIdentifier = fileIdentifier,
                                ContentType = "image/jpeg",
                                FileSize = imageBytes.Length,
                                OriginalFileName = "avatar.jpg",
                                Hash = hash,
                                CreatedAtUtc = DateTime.UtcNow,
                                IsPublic = true,
                                UploaderId = user.Id
                            });

                            user.AvatarStorageIdentifier = fileIdentifier;
                            fetchedCount++;
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }
                    catch
                    {
                        skippedCount++;
                    }

                    task.Increment(1);
                }
            });

        // Save all StoredFile records and user updates
        if (storedFileBatch.Count > 0)
        {
            _nuuru.StoredFiles.AddRange(storedFileBatch);
        }
        await _nuuru.SaveChangesAsync(ct);

        AnsiConsole.MarkupLine($"[green]Gravatar avatars: {fetchedCount} fetched, {skippedCount} not found[/]");
    }

    private static string GetMd5Hash(string input)
    {
        var inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hashBytes = System.Security.Cryptography.MD5.HashData(inputBytes);
        return Convert.ToHexStringLower(hashBytes);
    }

    private async Task MigrateTagCategoriesAsync(CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[yellow]Migrating tag categories...[/]");

        var categories = await _shimmie.GetTagCategoriesAsync(ct);
        var sortOrder = 0;

        foreach (var cat in categories)
        {
            var existing = await _nuuru.BooruTagCategories
                .FirstOrDefaultAsync(c => c.Slug == cat.Category.ToLowerInvariant(), ct);

            if (existing != null)
            {
                _tagCategoryMap[cat.Category] = existing.Id;
                continue;
            }

            var newCategory = new TagCategory
            {
                Id = Guid.NewGuid(),
                Name = cat.DisplaySingular ?? cat.Category,
                Slug = cat.Category.ToLowerInvariant(),
                ColorHex = cat.Color,
                SortOrder = sortOrder++,
                IsActive = true
            };

            _nuuru.BooruTagCategories.Add(newCategory);
            _tagCategoryMap[cat.Category] = newCategory.Id;
        }

        await _nuuru.SaveChangesAsync(ct);
        AnsiConsole.MarkupLine($"[green]Tag categories migration complete: {_tagCategoryMap.Count} categories[/]");
    }

    private async Task MigrateTagsAsync(CancellationToken ct)
    {
        var tags = await _shimmie.GetTagsAsync(ct);
        var totalTags = tags.Count;
        AnsiConsole.MarkupLine($"[yellow]Migrating {totalTags} tags...[/]");

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Migrating tags[/]", maxValue: totalTags);
                var batch = new List<Tag>();

                foreach (var shimmieTag in tags)
                {
                    // Parse category prefix (e.g., "artist:name" -> category: "artist", name: "name")
                    var (categorySlug, tagName) = ParseTagWithCategory(shimmieTag.Tag);

                    var existing = await _nuuru.BooruTags
                        .FirstOrDefaultAsync(t => t.Name == tagName, ct);

                    if (existing != null)
                    {
                        _tagIdMap[shimmieTag.Id] = existing.Id;
                        task.Increment(1);
                        continue;
                    }

                    Guid? categoryId = null;
                    if (categorySlug != null && _tagCategoryMap.TryGetValue(categorySlug, out var catId))
                    {
                        categoryId = catId;
                    }

                    var newTag = new Tag
                    {
                        Id = Guid.NewGuid(),
                        Name = tagName,
                        PostCount = 0 // Will be updated later
                    };

                    if (categoryId.HasValue)
                    {
                        newTag.Category = await _nuuru.BooruTagCategories.FindAsync([categoryId.Value], ct);
                    }
                    newTag.CreatedAt = DateTime.UtcNow;

                    batch.Add(newTag);
                    _tagIdMap[shimmieTag.Id] = newTag.Id;

                    if (batch.Count >= _options.BatchSize)
                    {
                        _nuuru.BooruTags.AddRange(batch);
                        await _nuuru.SaveChangesAsync(ct);
                        batch.Clear();
                    }

                    task.Increment(1);
                }

                if (batch.Count > 0)
                {
                    _nuuru.BooruTags.AddRange(batch);
                    await _nuuru.SaveChangesAsync(ct);
                }
            });

        AnsiConsole.MarkupLine($"[green]Tags migration complete: {_tagIdMap.Count} tags migrated[/]");
    }

    private async Task MigrateTagAliasesAsync(CancellationToken ct)
    {
        var aliases = await _shimmie.GetAliasesAsync(ct);
        var totalAliases = aliases.Count;
        AnsiConsole.MarkupLine($"[yellow]Migrating {totalAliases} tag aliases...[/]");

        var migratedCount = 0;
        var addedAliasTagIds = new HashSet<Guid>(); // Track in-memory adds

        foreach (var alias in aliases)
        {
            var (_, oldTagName) = ParseTagWithCategory(alias.OldTag);
            var (_, newTagName) = ParseTagWithCategory(alias.NewTag);

            var sourceTag = await _nuuru.BooruTags.FirstOrDefaultAsync(t => t.Name == oldTagName, ct);
            var targetTag = await _nuuru.BooruTags.FirstOrDefaultAsync(t => t.Name == newTagName, ct);

            if (sourceTag == null || targetTag == null)
                continue;

            // Check if alias already added in this batch
            if (addedAliasTagIds.Contains(sourceTag.Id))
                continue;

            // Check if alias already exists in database (unique constraint on AliasTagId)
            var existingAlias = await _nuuru.BooruTagAliases
                .FirstOrDefaultAsync(a => a.AliasTagId == sourceTag.Id, ct);

            if (existingAlias != null)
                continue;

            var newAlias = new TagAlias
            {
                Id = Guid.NewGuid(),
                AliasTagId = sourceTag.Id,
                TargetTagId = targetTag.Id,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _nuuru.BooruTagAliases.Add(newAlias);
            addedAliasTagIds.Add(sourceTag.Id);
            migratedCount++;
        }

        await _nuuru.SaveChangesAsync(ct);
        AnsiConsole.MarkupLine($"[green]Tag aliases migration complete: {migratedCount} aliases migrated[/]");
    }

    private async Task MigrateTagImplicationsAsync(CancellationToken ct)
    {
        var autoTags = await _shimmie.GetAutoTagsAsync(ct);
        var totalAutoTags = autoTags.Count;
        AnsiConsole.MarkupLine($"[yellow]Migrating {totalAutoTags} auto-tag rules to implications...[/]");

        var migratedCount = 0;

        foreach (var autoTag in autoTags)
        {
            // Parse the trigger tag
            var (_, triggerTagName) = ParseTagWithCategory(autoTag.Tag);
            var triggerTag = await _nuuru.BooruTags.FirstOrDefaultAsync(t => t.Name == triggerTagName, ct);

            if (triggerTag == null)
                continue;

            // Parse additional_tags (space or comma separated)
            var impliedTagNames = autoTag.AdditionalTags
                .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            foreach (var impliedTagRaw in impliedTagNames)
            {
                var (_, impliedTagName) = ParseTagWithCategory(impliedTagRaw);
                var impliedTag = await _nuuru.BooruTags.FirstOrDefaultAsync(t => t.Name == impliedTagName, ct);

                if (impliedTag == null)
                    continue;

                // Check if implication already exists
                var existingImplication = await _nuuru.BooruTagImplications
                    .FirstOrDefaultAsync(i => i.AntecedentTagId == triggerTag.Id && i.ConsequentTagId == impliedTag.Id, ct);

                if (existingImplication != null)
                    continue;

                var newImplication = new TagImplication
                {
                    Id = Guid.NewGuid(),
                    AntecedentTagId = triggerTag.Id,
                    ConsequentTagId = impliedTag.Id,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                _nuuru.BooruTagImplications.Add(newImplication);
                migratedCount++;
            }
        }

        await _nuuru.SaveChangesAsync(ct);
        AnsiConsole.MarkupLine($"[green]Tag implications migration complete: {migratedCount} implications migrated[/]");
    }

    private async Task MigratePostsAsync(CancellationToken ct)
    {
        var totalPosts = await _shimmie.GetImageCountAsync(_options.SkipTrash, ct);
        AnsiConsole.MarkupLine($"[yellow]Migrating {totalPosts} posts (parallelism: {_options.Parallelism})...[/]");

        // Pre-load existing post IDs to avoid N+1 queries
        var existingPostIds = await _nuuru.BooruPosts.Select(p => p.Id).ToHashSetAsync(ct);

        var uploadsPath = Path.GetFullPath(_options.NuuruUploadsPath);

        var migratedCount = 0;
        var skippedCount = 0;
        var errorCount = 0;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Migrating posts[/]", maxValue: totalPosts);

                // Buffer posts into chunks for parallel file processing
                var chunk = new List<(ShimmieImage post, Guid uploaderId)>();
                var allImages = await _shimmie.GetImagesAsync(_options.SkipTrash, ct);

                foreach (var shimmiePost in allImages)
                {
                    if (!_userIdMap.TryGetValue(shimmiePost.OwnerId, out var uploaderId))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    var postId = _options.PreservePostIds ? shimmiePost.Id : 0;
                    if (_options.PreservePostIds && existingPostIds.Contains(postId))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    existingPostIds.Add(postId);
                    chunk.Add((shimmiePost, uploaderId));

                    if (chunk.Count >= _options.BatchSize)
                    {
                        var result = await ProcessPostChunkAsync(chunk, uploadsPath, task, ct);
                        migratedCount += result.migrated;
                        errorCount += result.errors;
                        chunk.Clear();
                    }
                }

                // Process remaining chunk
                if (chunk.Count > 0)
                {
                    var result = await ProcessPostChunkAsync(chunk, uploadsPath, task, ct);
                    migratedCount += result.migrated;
                    errorCount += result.errors;
                }
            });

        AnsiConsole.MarkupLine($"[green]Posts migration complete: {migratedCount} migrated, {skippedCount} skipped, {errorCount} errors[/]");
    }

    private async Task<(int migrated, int errors)> ProcessPostChunkAsync(
        List<(ShimmieImage post, Guid uploaderId)> chunk,
        string uploadsPath,
        ProgressTask progressTask,
        CancellationToken ct)
    {
        // Phase 1: Process files in parallel (no DbContext access)
        var results = new ConcurrentBag<(ShimmieImage post, Guid uploaderId, FileProcessResult result)>();
        var errors = 0;

        await Parallel.ForEachAsync(chunk, new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.CopyFiles ? _options.Parallelism : chunk.Count,
            CancellationToken = ct
        }, async (item, token) =>
        {
            try
            {
                var result = await ProcessPostFileAsync(item.post, item.uploaderId, uploadsPath, token);
                if (result != null)
                    results.Add((item.post, item.uploaderId, result));
                else
                    Interlocked.Increment(ref errors);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error processing file for post {item.post.Id}: {ex.Message}[/]");
                Interlocked.Increment(ref errors);
            }
        });

        // Phase 2: Batch insert all DB records (single-threaded, fast)
        var storedFiles = new List<StoredFile>();
        var posts = new List<Post>();

        foreach (var (shimmiePost, uploaderId, fileResult) in results)
        {
            // StoredFile for the main file
            storedFiles.Add(new StoredFile
            {
                Id = Guid.NewGuid(),
                FileIdentifier = fileResult.FileIdentifier,
                ContentType = shimmiePost.Mime ?? GetMimeType(shimmiePost.Ext),
                FileSize = fileResult.FileSize,
                OriginalFileName = shimmiePost.Filename,
                Hash = fileResult.Hash,
                CreatedAtUtc = DateTime.UtcNow,
                IsPublic = true,
                UploaderId = uploaderId
            });

            // StoredFile for the thumbnail
            if (fileResult.ThumbnailIdentifier != null)
            {
                storedFiles.Add(new StoredFile
                {
                    Id = Guid.NewGuid(),
                    FileIdentifier = fileResult.ThumbnailIdentifier,
                    ContentType = fileResult.ThumbnailContentType!,
                    FileSize = fileResult.ThumbnailFileSize,
                    OriginalFileName = $"thumbnail.{fileResult.ThumbnailExtension}",
                    Hash = fileResult.ThumbnailHash!,
                    CreatedAtUtc = DateTime.UtcNow,
                    IsPublic = true,
                    UploaderId = uploaderId
                });
            }

            var postId = _options.PreservePostIds ? shimmiePost.Id : 0;
            posts.Add(new Post
            {
                Id = postId,
                StorageIdentifier = fileResult.FileIdentifier,
                ImageHash = fileResult.Hash,
                MimeType = shimmiePost.Mime ?? GetMimeType(shimmiePost.Ext),
                FileSize = fileResult.FileSize,
                OriginalFileName = shimmiePost.Filename,
                Source = shimmiePost.Source,
                Width = shimmiePost.Width,
                Height = shimmiePost.Height,
                DurationSeconds = fileResult.DurationSeconds,
                ThumbnailPath = fileResult.ThumbnailIdentifier,
                UploadedAt = DateTime.SpecifyKind(shimmiePost.Posted, DateTimeKind.Utc),
                Rating = MapRating(shimmiePost.Rating),
                IsApproved = shimmiePost.Approved,
                ApprovedById = shimmiePost.ApprovedById.HasValue && _userIdMap.TryGetValue(shimmiePost.ApprovedById.Value, out var approverId) ? approverId : null,
                ApprovedAt = shimmiePost.Approved ? DateTime.SpecifyKind(shimmiePost.Posted, DateTimeKind.Utc) : null,
                UploaderId = uploaderId
            });
        }

        if (storedFiles.Count > 0)
            _nuuru.StoredFiles.AddRange(storedFiles);
        if (posts.Count > 0)
            _nuuru.BooruPosts.AddRange(posts);
        if (storedFiles.Count > 0 || posts.Count > 0)
            await _nuuru.SaveChangesAsync(ct);

        progressTask.Increment(chunk.Count);
        return (posts.Count, errors);
    }

    private async Task MigratePostTagsAsync(CancellationToken ct)
    {
        var postTags = await _shimmie.GetImageTagsAsync(ct);
        var totalPostTags = postTags.Count;
        AnsiConsole.MarkupLine($"[yellow]Migrating {totalPostTags} post-tag relationships...[/]");

        // Pre-load existing post IDs
        var existingPostIds = await _nuuru.BooruPosts.Select(p => p.Id).ToHashSetAsync(ct);

        // Pre-load existing post-tag combinations
        var existingPostTags = await _nuuru.Set<PostTag>()
            .Select(pt => new { pt.PostId, pt.TagId })
            .ToListAsync(ct);
        var existingPostTagSet = existingPostTags.Select(x => (x.PostId, x.TagId)).ToHashSet();

        var batch = new List<PostTag>();
        var migratedCount = 0;
        var skippedCount = 0;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Migrating post tags[/]", maxValue: totalPostTags);

                foreach (var pt in postTags)
                {
                    if (!_tagIdMap.TryGetValue(pt.TagId, out var tagId))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    // Check post exists using pre-loaded set
                    if (!existingPostIds.Contains(pt.ImageId))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    // Check if post-tag already exists (in DB or current batch)
                    var key = (pt.ImageId, tagId);
                    if (existingPostTagSet.Contains(key))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    batch.Add(new PostTag
                    {
                        PostId = pt.ImageId,
                        TagId = tagId,
                        AddedAt = DateTime.UtcNow
                    });
                    existingPostTagSet.Add(key); // Track to prevent duplicates in batch

                    migratedCount++;

                    if (batch.Count >= _options.BatchSize)
                    {
                        _nuuru.Set<PostTag>().AddRange(batch);
                        await _nuuru.SaveChangesAsync(ct);
                        batch.Clear();
                    }

                    task.Increment(1);
                }

                if (batch.Count > 0)
                {
                    _nuuru.Set<PostTag>().AddRange(batch);
                    await _nuuru.SaveChangesAsync(ct);
                }
            });

        AnsiConsole.MarkupLine($"[green]Post tags migration complete: {migratedCount} migrated, {skippedCount} skipped[/]");
    }

    private async Task MigrateCommentsAsync(CancellationToken ct)
    {
        var comments = await _shimmie.GetCommentsAsync(ct);
        var totalComments = comments.Count;
        AnsiConsole.MarkupLine($"[yellow]Migrating {totalComments} comments...[/]");

        // Pre-load existing data
        var existingPostIds = await _nuuru.BooruPosts.Select(p => p.Id).ToHashSetAsync(ct);
        var existingCommentIds = await _nuuru.BooruComments.Select(c => c.Id).ToHashSetAsync(ct);

        var batch = new List<Comment>();
        var migratedCount = 0;
        var skippedCount = 0;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Migrating comments[/]", maxValue: totalComments);

                foreach (var comment in comments)
                {
                    if (!_userIdMap.TryGetValue(comment.OwnerId, out var userId))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    if (!existingPostIds.Contains(comment.ImageId))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    if (existingCommentIds.Contains(comment.Id))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    // Transform Shimmie BBCode to Nuuru format and parse to HTML
                    var transformedBBCode = TransformShimmieBBCode(comment.Comment);
                    var contentHtml = _bbCodeService.Parse(transformedBBCode);

                    batch.Add(new Comment
                    {
                        Id = comment.Id,
                        PostId = comment.ImageId,
                        UserId = userId,
                        ContentRaw = transformedBBCode,
                        ContentHtml = contentHtml,
                        CreatedAt = DateTime.SpecifyKind(comment.Posted, DateTimeKind.Utc)
                    });
                    existingCommentIds.Add(comment.Id);
                    migratedCount++;

                    if (batch.Count >= _options.BatchSize)
                    {
                        _nuuru.BooruComments.AddRange(batch);
                        await _nuuru.SaveChangesAsync(ct);
                        batch.Clear();
                    }

                    task.Increment(1);
                }

                if (batch.Count > 0)
                {
                    _nuuru.BooruComments.AddRange(batch);
                    await _nuuru.SaveChangesAsync(ct);
                }
            });

        AnsiConsole.MarkupLine($"[green]Comments migration complete: {migratedCount} migrated, {skippedCount} skipped[/]");
    }

    private async Task MigrateFavoritesAsync(CancellationToken ct)
    {
        var favorites = await _shimmie.GetFavoritesAsync(ct);
        var totalFavorites = favorites.Count;
        AnsiConsole.MarkupLine($"[yellow]Migrating {totalFavorites} favorites...[/]");

        // Pre-load existing data
        var existingPostIds = await _nuuru.BooruPosts.Select(p => p.Id).ToHashSetAsync(ct);
        var existingFavorites = await _nuuru.BooruPostFavorites
            .Select(f => new { f.PostId, f.UserId })
            .ToListAsync(ct);
        var existingFavSet = existingFavorites.Select(x => (x.PostId, x.UserId)).ToHashSet();

        var batch = new List<PostFavorite>();
        var migratedCount = 0;
        var skippedCount = 0;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Migrating favorites[/]", maxValue: totalFavorites);

                foreach (var fav in favorites)
                {
                    if (!_userIdMap.TryGetValue(fav.UserId, out var userId))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    if (!existingPostIds.Contains(fav.ImageId))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    var key = (fav.ImageId, userId);
                    if (existingFavSet.Contains(key))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    batch.Add(new PostFavorite
                    {
                        PostId = fav.ImageId,
                        UserId = userId,
                        CreatedAt = DateTime.SpecifyKind(fav.CreatedAt, DateTimeKind.Utc)
                    });
                    existingFavSet.Add(key);
                    migratedCount++;

                    if (batch.Count >= _options.BatchSize)
                    {
                        _nuuru.BooruPostFavorites.AddRange(batch);
                        await _nuuru.SaveChangesAsync(ct);
                        batch.Clear();
                    }

                    task.Increment(1);
                }

                if (batch.Count > 0)
                {
                    _nuuru.BooruPostFavorites.AddRange(batch);
                    await _nuuru.SaveChangesAsync(ct);
                }
            });

        AnsiConsole.MarkupLine($"[green]Favorites migration complete: {migratedCount} migrated, {skippedCount} skipped[/]");
    }

    private async Task MigrateVotesAsync(CancellationToken ct)
    {
        var votes = await _shimmie.GetVotesAsync(ct);
        var totalVotes = votes.Count;
        AnsiConsole.MarkupLine($"[yellow]Migrating {totalVotes} votes...[/]");

        // Pre-load existing data (include post upload dates as fallback for vote timestamps)
        var postUploadDates = await _nuuru.BooruPosts
            .Select(p => new { p.Id, p.UploadedAt })
            .ToDictionaryAsync(p => p.Id, p => p.UploadedAt, ct);
        var existingPostIds = postUploadDates.Keys.ToHashSet();
        var existingVotes = await _nuuru.BooruPostVotes
            .Select(v => new { v.PostId, v.UserId })
            .ToListAsync(ct);
        var existingVoteSet = existingVotes.Select(x => (x.PostId, x.UserId)).ToHashSet();

        var batch = new List<PostVote>();
        var migratedCount = 0;
        var skippedCount = 0;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Migrating votes[/]", maxValue: totalVotes);

                foreach (var vote in votes)
                {
                    if (!_userIdMap.TryGetValue(vote.UserId, out var userId))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    if (!existingPostIds.Contains(vote.ImageId))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    var key = (vote.ImageId, userId);
                    if (existingVoteSet.Contains(key))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    // Shimmie uses arbitrary scores, Nuuru uses -1/+1
                    var normalizedValue = vote.Score > 0 ? 1 : (vote.Score < 0 ? -1 : 0);
                    if (normalizedValue == 0)
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    batch.Add(new PostVote
                    {
                        PostId = vote.ImageId,
                        UserId = userId,
                        Value = normalizedValue,
                        // Shimmie doesn't store vote timestamps; use the post's upload date as a proxy
                        CreatedAt = postUploadDates.TryGetValue(vote.ImageId, out var uploadedAt)
                            ? uploadedAt
                            : DateTime.UtcNow
                    });
                    existingVoteSet.Add(key);
                    migratedCount++;

                    if (batch.Count >= _options.BatchSize)
                    {
                        _nuuru.BooruPostVotes.AddRange(batch);
                        await _nuuru.SaveChangesAsync(ct);
                        batch.Clear();
                    }

                    task.Increment(1);
                }

                if (batch.Count > 0)
                {
                    _nuuru.BooruPostVotes.AddRange(batch);
                    await _nuuru.SaveChangesAsync(ct);
                }
            });

        AnsiConsole.MarkupLine($"[green]Votes migration complete: {migratedCount} migrated, {skippedCount} skipped[/]");
    }

    private async Task MigratePrivateMessagesAsync(CancellationToken ct)
    {
        var pms = await _shimmie.GetPrivateMessagesAsync(ct);
        if (pms.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No private messages to migrate[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[yellow]Migrating {pms.Count} private messages...[/]");

        // Pre-load existing conversations with participants to avoid N+1 queries
        // Include 1-participant conversations (self-PMs) and 2-participant conversations
        var existingConversationPairs = await _nuuru.Set<Conversation>()
            .Where(c => c.Participants.Count >= 1 && c.Participants.Count <= 2)
            .Select(c => new {
                User1 = c.Participants.OrderBy(p => p.UserId).First().UserId,
                User2 = c.Participants.OrderBy(p => p.UserId).Last().UserId
            })
            .ToListAsync(ct);
        var existingPairSet = existingConversationPairs
            .Select(x => (x.User1, x.User2))
            .ToHashSet();

        // Group PMs by unique user pair (sorted to ensure consistent grouping regardless of direction)
        var conversationGroups = pms
            .Where(pm => _userIdMap.ContainsKey(pm.FromId) && _userIdMap.ContainsKey(pm.ToId))
            .GroupBy(pm =>
            {
                var id1 = Math.Min(pm.FromId, pm.ToId);
                var id2 = Math.Max(pm.FromId, pm.ToId);
                return (id1, id2);
            })
            .ToList();

        var conversationsCreated = 0;
        var messagesCreated = 0;
        var skippedCount = 0;
        var batchCount = 0;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Migrating private messages[/]", maxValue: conversationGroups.Count);

                foreach (var group in conversationGroups)
                {
                    var user1Id = _userIdMap[group.Key.id1];
                    var user2Id = _userIdMap[group.Key.id2];

                    // Sort for consistent lookup
                    var sortedPair = user1Id.CompareTo(user2Id) < 0
                        ? (user1Id, user2Id)
                        : (user2Id, user1Id);

                    if (existingPairSet.Contains(sortedPair))
                    {
                        skippedCount += group.Count();
                        task.Increment(1);
                        continue;
                    }

                    var orderedPMs = group.OrderBy(pm => pm.SentDate).ToList();
                    var firstPM = orderedPMs.First();
                    var creatorId = _userIdMap[firstPM.FromId];

                    var conversation = new Conversation
                    {
                        Id = Guid.NewGuid(),
                        Title = null,
                        CreatedAt = DateTime.SpecifyKind(firstPM.SentDate, DateTimeKind.Utc),
                        LastMessageAt = DateTime.SpecifyKind(orderedPMs.Last().SentDate, DateTimeKind.Utc),
                        MessageCount = orderedPMs.Count,
                        CreatorId = creatorId
                    };

                    _nuuru.Set<Conversation>().Add(conversation);
                    existingPairSet.Add(sortedPair);
                    conversationsCreated++;

                    // Mark all migrated conversations as read for both participants
                    _nuuru.Set<ConversationParticipant>().Add(new ConversationParticipant
                    {
                        ConversationId = conversation.Id,
                        UserId = user1Id,
                        JoinedAt = DateTime.SpecifyKind(firstPM.SentDate, DateTimeKind.Utc),
                        LastReadAt = conversation.LastMessageAt,
                        HasLeft = false
                    });

                    // Only add second participant if it's a different user (not a self-PM)
                    if (user1Id != user2Id)
                    {
                        _nuuru.Set<ConversationParticipant>().Add(new ConversationParticipant
                        {
                            ConversationId = conversation.Id,
                            UserId = user2Id,
                            JoinedAt = DateTime.SpecifyKind(firstPM.SentDate, DateTimeKind.Utc),
                            LastReadAt = conversation.LastMessageAt,
                            HasLeft = false
                        });
                    }

                    foreach (var pm in orderedPMs)
                    {
                        var rawContent = !string.IsNullOrWhiteSpace(pm.Subject)
                            ? $"[b]{pm.Subject}[/b]\n\n{pm.Message}"
                            : pm.Message;

                        var transformedBBCode = TransformShimmieBBCode(rawContent);
                        var contentHtml = _bbCodeService.Parse(transformedBBCode);

                        _nuuru.Set<Message>().Add(new Message
                        {
                            ConversationId = conversation.Id,
                            AuthorId = _userIdMap[pm.FromId],
                            ContentRaw = transformedBBCode,
                            ContentHtml = contentHtml,
                            CreatedAt = DateTime.SpecifyKind(pm.SentDate, DateTimeKind.Utc),
                            EditedAt = null
                        });
                        messagesCreated++;
                    }

                    batchCount++;
                    if (batchCount >= _options.BatchSize)
                    {
                        await _nuuru.SaveChangesAsync(ct);
                        batchCount = 0;
                    }

                    task.Increment(1);
                }

                if (batchCount > 0)
                {
                    await _nuuru.SaveChangesAsync(ct);
                }
            });

        AnsiConsole.MarkupLine($"[green]Private messages migration complete: {conversationsCreated} conversations, {messagesCreated} messages, {skippedCount} skipped[/]");
    }

    private async Task MigrateTagHistoriesAsync(CancellationToken ct)
    {
        var histories = await _shimmie.GetTagHistoriesAsync(ct);
        if (histories.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No tag histories to migrate[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[yellow]Migrating {histories.Count} tag history entries...[/]");

        // Pre-load existing post IDs and tag history keys for dedup
        var existingPostIds = await _nuuru.BooruPosts.Select(p => p.Id).ToHashSetAsync(ct);
        var existingTagHistoryKeys = (await _nuuru.TagHistories
            .Select(h => new { h.PostId, h.DateSet, h.Tags })
            .ToListAsync(ct))
            .Select(h => (h.PostId, h.DateSet, h.Tags))
            .ToHashSet();

        var lastTagHistoryDate = new Dictionary<int, DateTime>();
        var batch = new List<TagHistory>();
        var migratedCount = 0;
        var skippedCount = 0;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Migrating tag histories[/]", maxValue: histories.Count);

                foreach (var history in histories)
                {
                    if (!_userIdMap.TryGetValue(history.UserId, out var userId))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    if (!existingPostIds.Contains(history.ImageId))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    var dateSet = DateTime.SpecifyKind(history.DateSet, DateTimeKind.Utc);

                    // Ensure dates are monotonically increasing per post (higher ID = same or later date)
                    if (lastTagHistoryDate.TryGetValue(history.ImageId, out var lastDate) && dateSet <= lastDate)
                    {
                        dateSet = lastDate.AddSeconds(1);
                    }
                    lastTagHistoryDate[history.ImageId] = dateSet;

                    var key = (history.ImageId, dateSet, history.Tags);
                    if (existingTagHistoryKeys.Contains(key))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }
                    existingTagHistoryKeys.Add(key);

                    batch.Add(new TagHistory
                    {
                        PostId = history.ImageId,
                        UserId = userId,
                        UserIp = history.UserIp,
                        Tags = history.Tags,
                        DateSet = dateSet
                    });
                    migratedCount++;

                    if (batch.Count >= _options.BatchSize)
                    {
                        _nuuru.TagHistories.AddRange(batch);
                        await _nuuru.SaveChangesAsync(ct);
                        batch.Clear();
                    }

                    task.Increment(1);
                }

                if (batch.Count > 0)
                {
                    _nuuru.TagHistories.AddRange(batch);
                    await _nuuru.SaveChangesAsync(ct);
                }
            });

        AnsiConsole.MarkupLine($"[green]Tag histories migration complete: {migratedCount} migrated, {skippedCount} skipped[/]");
    }

    private async Task MigrateSourceHistoriesAsync(CancellationToken ct)
    {
        var histories = await _shimmie.GetSourceHistoriesAsync(ct);
        if (histories.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No source histories to migrate[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[yellow]Migrating {histories.Count} source history entries...[/]");

        // Pre-load existing post IDs and source history keys for dedup
        var existingPostIds = await _nuuru.BooruPosts.Select(p => p.Id).ToHashSetAsync(ct);
        var existingSourceHistoryKeys = (await _nuuru.SourceHistories
            .Select(h => new { h.PostId, h.DateSet, h.Source })
            .ToListAsync(ct))
            .Select(h => (h.PostId, h.DateSet, h.Source))
            .ToHashSet();

        var lastSourceHistoryDate = new Dictionary<int, DateTime>();
        var batch = new List<SourceHistory>();
        var migratedCount = 0;
        var skippedCount = 0;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Migrating source histories[/]", maxValue: histories.Count);

                foreach (var history in histories)
                {
                    if (!_userIdMap.TryGetValue(history.UserId, out var userId))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    if (!existingPostIds.Contains(history.ImageId))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }

                    var dateSet = DateTime.SpecifyKind(history.DateSet, DateTimeKind.Utc);

                    // Ensure dates are monotonically increasing per post (higher ID = same or later date)
                    if (lastSourceHistoryDate.TryGetValue(history.ImageId, out var lastDate) && dateSet <= lastDate)
                    {
                        dateSet = lastDate.AddSeconds(1);
                    }
                    lastSourceHistoryDate[history.ImageId] = dateSet;

                    var key = (history.ImageId, dateSet, history.Source);
                    if (existingSourceHistoryKeys.Contains(key))
                    {
                        skippedCount++;
                        task.Increment(1);
                        continue;
                    }
                    existingSourceHistoryKeys.Add(key);

                    batch.Add(new SourceHistory
                    {
                        PostId = history.ImageId,
                        UserId = userId,
                        UserIp = history.UserIp,
                        Source = history.Source,
                        DateSet = dateSet
                    });
                    migratedCount++;

                    if (batch.Count >= _options.BatchSize)
                    {
                        _nuuru.SourceHistories.AddRange(batch);
                        await _nuuru.SaveChangesAsync(ct);
                        batch.Clear();
                    }

                    task.Increment(1);
                }

                if (batch.Count > 0)
                {
                    _nuuru.SourceHistories.AddRange(batch);
                    await _nuuru.SaveChangesAsync(ct);
                }
            });

        AnsiConsole.MarkupLine($"[green]Source histories migration complete: {migratedCount} migrated, {skippedCount} skipped[/]");
    }

    private async Task UpdateTagCountsAsync(CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[yellow]Updating tag counts...[/]");

        var tags = await _nuuru.BooruTags.ToListAsync(ct);

        foreach (var tag in tags)
        {
            tag.PostCount = await _nuuru.Set<PostTag>().CountAsync(pt => pt.TagId == tag.Id, ct);
        }

        await _nuuru.SaveChangesAsync(ct);
        AnsiConsole.MarkupLine("[green]Tag counts updated[/]");
    }

    private async Task ResetPostgresSequencesAsync(CancellationToken ct)
    {
        if (!_nuuru.Database.IsNpgsql())
            return;

        AnsiConsole.MarkupLine("[yellow]Resetting PostgreSQL identity sequences...[/]");

        var tables = new[] { "BooruPosts", "BooruComments", "Messages", "TagHistories", "SourceHistories" };

        foreach (var table in tables)
        {
            await _nuuru.Database.ExecuteSqlRawAsync(
                $"SELECT setval(pg_get_serial_sequence('\"{table}\"', 'Id'), COALESCE((SELECT MAX(\"Id\") FROM \"{table}\"), 0) + 1, false)", ct);
        }

        AnsiConsole.MarkupLine("[green]PostgreSQL identity sequences reset[/]");
    }

    private record FileProcessResult(
        string FileIdentifier, string Hash, long FileSize,
        string? ThumbnailIdentifier, string? ThumbnailHash, string? ThumbnailContentType,
        string? ThumbnailExtension, long ThumbnailFileSize, int? DurationSeconds);

    /// <summary>
    /// Processes a post's file entirely without DbContext: copies file, computes hash, generates thumbnail.
    /// Thread-safe — can be called from Parallel.ForEachAsync.
    /// </summary>
    private async Task<FileProcessResult?> ProcessPostFileAsync(
        ShimmieImage post, Guid uploaderId, string uploadsPath, CancellationToken ct)
    {
        if (!_options.CopyFiles)
            return null;

        // Resolve source file path
        var hashPrefix = post.Hash.Substring(0, 2);
        var sourceImagePath = Path.Combine(_options.ShimmieImagesPath, hashPrefix, post.Hash);
        if (!File.Exists(sourceImagePath))
            sourceImagePath = $"{sourceImagePath}.{post.Ext}";
        if (!File.Exists(sourceImagePath))
        {
            AnsiConsole.MarkupLine($"[red]Source file not found for post {post.Id}: {sourceImagePath}[/]");
            return null;
        }

        // Copy file to Nuuru uploads and compute SHA-256 in a single pass
        var fileIdentifier = Guid.NewGuid().ToString("N");
        var subDir = Path.Combine(uploadsPath, fileIdentifier.Substring(0, 2));
        Directory.CreateDirectory(subDir);
        var destPath = Path.Combine(subDir, fileIdentifier);

        string hash;
        long fileSize;
        await using (var source = new FileStream(sourceImagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write))
        using (var sha256 = SHA256.Create())
        {
            var buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, ct)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            }
            sha256.TransformFinalBlock([], 0, 0);
            hash = Convert.ToHexStringLower(sha256.Hash!);
            fileSize = dest.Length;
        }

        // Generate thumbnail
        var mimeType = post.Mime ?? GetMimeType(post.Ext);
        string? thumbIdentifier = null;
        string? thumbHash = null;
        string? thumbContentType = null;
        string? thumbExtension = null;
        long thumbFileSize = 0;
        int? durationSeconds = null;

        if (ThumbnailImageTypes.Contains(mimeType))
        {
            var thumbResult = await GenerateImageThumbnailAsync(sourceImagePath, uploadsPath);
            if (thumbResult != null)
            {
                thumbIdentifier = thumbResult.Value.identifier;
                thumbHash = thumbResult.Value.hash;
                thumbContentType = thumbResult.Value.contentType;
                thumbExtension = thumbResult.Value.extension;
                thumbFileSize = thumbResult.Value.fileSize;
            }
        }
        else if (ThumbnailVideoTypes.Contains(mimeType))
        {
            var thumbResult = await GenerateVideoThumbnailAsync(sourceImagePath, uploadsPath);
            if (thumbResult != null)
            {
                thumbIdentifier = thumbResult.Value.identifier;
                thumbHash = thumbResult.Value.hash;
                thumbContentType = thumbResult.Value.contentType;
                thumbExtension = thumbResult.Value.extension;
                thumbFileSize = thumbResult.Value.fileSize;
                durationSeconds = thumbResult.Value.durationSeconds;
            }
        }

        return new FileProcessResult(
            fileIdentifier, hash, fileSize,
            thumbIdentifier, thumbHash, thumbContentType, thumbExtension, thumbFileSize,
            durationSeconds);
    }

    private const int ThumbnailMaxWidth = 300;
    private const int ThumbnailMaxHeight = 300;
    private const int ThumbnailWebPQuality = 75;

    private static readonly HashSet<string> ThumbnailImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp"
    };

    private static readonly HashSet<string> ThumbnailVideoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "video/mp4", "video/webm", "video/quicktime"
    };

    private static async Task<(string identifier, string hash, string contentType, string extension, long fileSize)?>
        GenerateImageThumbnailAsync(string sourcePath, string uploadsPath)
    {
        var tempOutput = Path.ChangeExtension(Path.GetTempFileName(), ".webp");
        try
        {
            using var image = await Image.LoadAsync(sourcePath);
            bool isAnimated = image.Frames.Count > 1;

            var scaleFilter =
                $"scale='min({ThumbnailMaxWidth},iw)':'min({ThumbnailMaxHeight},ih)':force_original_aspect_ratio=decrease";

            if (isAnimated)
            {
                await FFMpegArguments
                    .FromFileInput(sourcePath)
                    .OutputToFile(tempOutput, overwrite: true, options => options
                        .WithCustomArgument($"-vf {scaleFilter}")
                        .WithCustomArgument($"-quality {ThumbnailWebPQuality} -loop 0")
                        .ForceFormat("webp"))
                    .ProcessAsynchronously();
            }
            else
            {
                await FFMpegArguments
                    .FromFileInput(sourcePath)
                    .OutputToFile(tempOutput, overwrite: true, options => options
                        .WithCustomArgument($"-vf {scaleFilter}")
                        .WithFrameOutputCount(1)
                        .WithCustomArgument($"-quality {ThumbnailWebPQuality}")
                        .ForceFormat("webp"))
                    .ProcessAsynchronously();
            }

            return await SaveThumbnailFileAsync(tempOutput, uploadsPath, "image/webp", "webp");
        }
        catch
        {
            return null;
        }
        finally
        {
            TryDeleteFile(tempOutput);
        }
    }

    private static async Task<(string identifier, string hash, string contentType, string extension, long fileSize, int? durationSeconds)?>
        GenerateVideoThumbnailAsync(string sourcePath, string uploadsPath)
    {
        var tempOutput = Path.ChangeExtension(Path.GetTempFileName(), ".webp");
        try
        {
            var mediaInfo = await FFProbe.AnalyseAsync(sourcePath);
            var duration = mediaInfo.Duration;
            var captureTime = TimeSpan.FromSeconds(Math.Min(1, duration.TotalSeconds * 0.1));

            var scaleFilter =
                $"scale='min({ThumbnailMaxWidth},iw)':'min({ThumbnailMaxHeight},ih)':force_original_aspect_ratio=decrease";

            await FFMpegArguments
                .FromFileInput(sourcePath)
                .OutputToFile(tempOutput, overwrite: true, options => options
                    .Seek(captureTime)
                    .WithCustomArgument($"-vf {scaleFilter}")
                    .WithFrameOutputCount(1)
                    .WithCustomArgument($"-quality {ThumbnailWebPQuality}")
                    .ForceFormat("webp"))
                .ProcessAsynchronously();

            var result = await SaveThumbnailFileAsync(tempOutput, uploadsPath, "image/webp", "webp");
            if (result == null) return null;

            return (result.Value.identifier, result.Value.hash, result.Value.contentType,
                    result.Value.extension, result.Value.fileSize, (int)duration.TotalSeconds);
        }
        catch
        {
            return null;
        }
        finally
        {
            TryDeleteFile(tempOutput);
        }
    }

    private static async Task<(string identifier, string hash, string contentType, string extension, long fileSize)?>
        SaveThumbnailFileAsync(string tempPath, string uploadsPath, string contentType, string extension)
    {
        var identifier = Guid.NewGuid().ToString("N");
        var subDir = Path.Combine(uploadsPath, identifier.Substring(0, 2));
        Directory.CreateDirectory(subDir);
        var destPath = Path.Combine(subDir, identifier);

        var data = await File.ReadAllBytesAsync(tempPath);
        await File.WriteAllBytesAsync(destPath, data);
        var hash = Convert.ToHexStringLower(SHA256.HashData(data));
        return (identifier, hash, contentType, extension, data.LongLength);
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private static string MapShimmieClassToRole(string shimmieClass)
    {
        return shimmieClass.ToLowerInvariant() switch
        {
            "admin" => "Admin",
            "mod" or "moderator" => "Moderator",
            "janitor" => "Janitor",
            "approver" => "Approver",
            "trusted" => "Trusted",
            "hellbanned" or "blocked" or "ghost" => "Banned",
            _ => "User"
        };
    }

    private static bool IsBannedClass(string shimmieClass)
    {
        return shimmieClass.ToLowerInvariant() is "hellbanned" or "blocked" or "ghost";
    }

    private static PostRating MapRating(string rating)
    {
        return rating?.ToLowerInvariant() switch
        {
            "s" => PostRating.Safe,
            "q" => PostRating.Questionable,
            "e" => PostRating.Explicit,
            _ => PostRating.Safe
        };
    }

    private static (string? category, string name) ParseTagWithCategory(string tag)
    {
        var colonIndex = tag.IndexOf(':');
        if (colonIndex > 0)
        {
            return (tag.Substring(0, colonIndex), tag.Substring(colonIndex + 1));
        }
        return (null, tag);
    }

    private static string GetMimeType(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "webm" => "video/webm",
            "mp4" => "video/mp4",
            "svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Transforms Shimmie BBCode to Nuuru BBCode format.
    /// - Converts [url=site://post/view/XXX#cYYY]@Username[/url] to [mention userguid=GUID]@Username[/mention]
    /// - Strips unsupported tags like [align=...]
    /// </summary>
    private string TransformShimmieBBCode(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = input;

        // Normalize line endings
        result = result.Replace("\r\n", "\n").Replace("\r", "\n");

        // Convert Shimmie mentions: [url=site://post/view/XXX#cYYY]@Username[/url] -> [mention userguid=GUID postid=XXX commentid=YYY]@Username[/mention]
        // Preserves the post/comment reference so the link navigates to the original comment
        result = MentionRegex.Replace(result, match =>
        {
            var postIdStr = match.Groups[1].Value;
            var commentIdStr = match.Groups[2].Value; // empty if no #cYYY
            var username = match.Groups[3].Value.Trim();
            if (_usernameToGuidMap.TryGetValue(username, out var userId))
            {
                var attrs = $"userguid={userId} postid={postIdStr}";
                if (!string.IsNullOrEmpty(commentIdStr))
                    attrs += $" commentid={commentIdStr}";
                return $"[mention {attrs}]@{username}[/mention]";
            }
            // User not found, just render as plain @username
            return $"@{username}";
        });

        // Strip [align=...] tags (not supported in Nuuru)
        result = AlignRegex.Replace(result, "$1");

        return result;
    }

    private static string EscapeHtml(string text)
    {
        return System.Net.WebUtility.HtmlEncode(text)
            .Replace("\n", "<br />");
    }
}
