using Microsoft.EntityFrameworkCore;

namespace Nuuru.Tools.ShimmieMigration.Source;

/// <summary>
/// EF Core-based data source for PostgreSQL and SQLite
/// </summary>
public class EfCoreShimmieDataSource : IShimmieDataSource
{
    private readonly ShimmieDbContext _context;

    public EfCoreShimmieDataSource(ShimmieDbContext context)
    {
        _context = context;
    }

    public async Task<List<string>> GetDistinctUserClassesAsync(CancellationToken ct = default)
    {
        return await _context.Users
            .Select(u => u.Class)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<List<ShimmieUser>> GetUsersAsync(CancellationToken ct = default)
    {
        return await _context.Users.OrderBy(u => u.Id).ToListAsync(ct);
    }

    public async Task<int> GetImageCountAsync(bool skipTrash, CancellationToken ct = default)
    {
        var query = _context.Images.AsQueryable();
        if (skipTrash)
        {
            query = query.Where(i => !i.Trash);
        }
        return await query.CountAsync(ct);
    }

    public async Task<List<ShimmieImage>> GetImagesAsync(bool skipTrash, CancellationToken ct = default)
    {
        var query = _context.Images.AsQueryable();
        if (skipTrash)
        {
            query = query.Where(i => !i.Trash);
        }

        return await query.OrderBy(i => i.Id).ToListAsync(ct);
    }

    public async Task<List<ShimmieTag>> GetTagsAsync(CancellationToken ct = default)
    {
        return await _context.Tags.ToListAsync(ct);
    }

    public async Task<List<ShimmieImageTag>> GetImageTagsAsync(CancellationToken ct = default)
    {
        return await _context.ImageTags.ToListAsync(ct);
    }

    public async Task<List<ShimmieComment>> GetCommentsAsync(CancellationToken ct = default)
    {
        return await _context.Comments.OrderBy(c => c.Id).ToListAsync(ct);
    }

    public async Task<List<ShimmieTagCategory>> GetTagCategoriesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.TagCategories.ToListAsync(ct);
        }
        catch
        {
            return new List<ShimmieTagCategory>();
        }
    }

    public async Task<List<ShimmieAlias>> GetAliasesAsync(CancellationToken ct = default)
    {
        return await _context.Aliases.ToListAsync(ct);
    }

    public async Task<List<ShimmieAutoTag>> GetAutoTagsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.AutoTags.ToListAsync(ct);
        }
        catch
        {
            return new List<ShimmieAutoTag>();
        }
    }

    public async Task<List<ShimmieFavorite>> GetFavoritesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.Favorites.ToListAsync(ct);
        }
        catch
        {
            return new List<ShimmieFavorite>();
        }
    }

    public async Task<List<ShimmieVote>> GetVotesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.Votes.ToListAsync(ct);
        }
        catch
        {
            return new List<ShimmieVote>();
        }
    }

    public async Task<List<ShimmiePM>> GetPrivateMessagesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.PrivateMessages.OrderBy(pm => pm.SentDate).ToListAsync(ct);
        }
        catch
        {
            return new List<ShimmiePM>();
        }
    }

    public async Task<List<ShimmieTagHistory>> GetTagHistoriesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.TagHistories.OrderBy(h => h.Id).ToListAsync(ct);
        }
        catch
        {
            return new List<ShimmieTagHistory>();
        }
    }

    public async Task<List<ShimmieSourceHistory>> GetSourceHistoriesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.SourceHistories.OrderBy(h => h.Id).ToListAsync(ct);
        }
        catch
        {
            return new List<ShimmieSourceHistory>();
        }
    }

    public ValueTask DisposeAsync()
    {
        return _context.DisposeAsync();
    }
}
