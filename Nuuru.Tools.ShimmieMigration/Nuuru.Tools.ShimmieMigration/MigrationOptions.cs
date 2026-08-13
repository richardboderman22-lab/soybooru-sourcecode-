namespace Nuuru.Tools.ShimmieMigration;

public class MigrationOptions
{
    public required string ShimmieConnectionString { get; set; }
    public required string NuuruConnectionString { get; set; }
    public DatabaseProvider ShimmieProvider { get; set; } = DatabaseProvider.MySQL;
    public DatabaseProvider NuuruProvider { get; set; } = DatabaseProvider.PostgreSQL;

    /// <summary>
    /// Path to Shimmie's images directory (contains hash-based subdirectories)
    /// </summary>
    public required string ShimmieImagesPath { get; set; }

    /// <summary>
    /// Path to Shimmie's thumbs directory
    /// </summary>
    public required string ShimmieThumbsPath { get; set; }

    /// <summary>
    /// Path to Nuuru's uploads directory
    /// </summary>
    public required string NuuruUploadsPath { get; set; }

    /// <summary>
    /// Batch size for database operations
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Whether to copy files or just reference them
    /// </summary>
    public bool CopyFiles { get; set; } = true;

    /// <summary>
    /// Skip images marked as trash
    /// </summary>
    public bool SkipTrash { get; set; } = true;

    /// <summary>
    /// Preserve original Shimmie post IDs in Nuuru
    /// </summary>
    public bool PreservePostIds { get; set; } = true;

    /// <summary>
    /// Fetch avatars from Gravatar for users with emails (non-banned only)
    /// </summary>
    public bool FetchGravatarAvatars { get; set; } = true;

    /// <summary>
    /// Number of concurrent file operations during post migration
    /// </summary>
    public int Parallelism { get; set; } = 4;

    /// <summary>
    /// Instead of full migration, only sync tags and tag histories from source
    /// for posts that haven't been modified in the target database.
    /// </summary>
    public bool SyncTagsOnly { get; set; } = false;
}

public enum DatabaseProvider
{
    MySQL,
    PostgreSQL,
    SQLite
}
