namespace Nuuru.Tools.ShimmieMigration.Source;

/// <summary>
/// Abstraction for reading Shimmie data, supporting both EF Core and raw MySQL
/// </summary>
public interface IShimmieDataSource : IAsyncDisposable
{
    Task<List<string>> GetDistinctUserClassesAsync(CancellationToken ct = default);
    Task<List<ShimmieUser>> GetUsersAsync(CancellationToken ct = default);
    Task<int> GetImageCountAsync(bool skipTrash, CancellationToken ct = default);
    Task<List<ShimmieImage>> GetImagesAsync(bool skipTrash, CancellationToken ct = default);
    Task<List<ShimmieTag>> GetTagsAsync(CancellationToken ct = default);
    Task<List<ShimmieImageTag>> GetImageTagsAsync(CancellationToken ct = default);
    Task<List<ShimmieComment>> GetCommentsAsync(CancellationToken ct = default);
    Task<List<ShimmieTagCategory>> GetTagCategoriesAsync(CancellationToken ct = default);
    Task<List<ShimmieAlias>> GetAliasesAsync(CancellationToken ct = default);
    Task<List<ShimmieAutoTag>> GetAutoTagsAsync(CancellationToken ct = default);
    Task<List<ShimmieFavorite>> GetFavoritesAsync(CancellationToken ct = default);
    Task<List<ShimmieVote>> GetVotesAsync(CancellationToken ct = default);
    Task<List<ShimmiePM>> GetPrivateMessagesAsync(CancellationToken ct = default);
    Task<List<ShimmieTagHistory>> GetTagHistoriesAsync(CancellationToken ct = default);
    Task<List<ShimmieSourceHistory>> GetSourceHistoriesAsync(CancellationToken ct = default);
}
