using MySqlConnector;

namespace Nuuru.Tools.ShimmieMigration.Source;

/// <summary>
/// Raw ADO.NET data reader for MySQL since Pomelo doesn't support .NET 10 yet
/// </summary>
public class MySqlShimmieDataReader : IShimmieDataSource
{
    private readonly string _connectionString;
    private MySqlConnection? _connection;

    public MySqlShimmieDataReader(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<MySqlConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        if (_connection == null)
        {
            _connection = new MySqlConnection(_connectionString);
            await _connection.OpenAsync(ct);
        }
        return _connection;
    }

    public async Task<List<ShimmieUser>> GetUsersAsync(CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var users = new List<ShimmieUser>();

        await using var cmd = new MySqlCommand("SELECT id, name, pass, joindate, class, email FROM users ORDER BY id", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            users.Add(new ShimmieUser
            {
                Id = reader.GetInt32("id"),
                Name = reader.GetString("name"),
                Pass = reader.IsDBNull(reader.GetOrdinal("pass")) ? null : reader.GetString("pass"),
                JoinDate = reader.GetDateTime("joindate"),
                Class = reader.GetString("class"),
                Email = reader.IsDBNull(reader.GetOrdinal("email")) ? null : reader.GetString("email")
            });
        }

        return users;
    }

    public async Task<int> GetImageCountAsync(bool skipTrash, CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var sql = skipTrash ? "SELECT COUNT(*) FROM images WHERE trash = 0" : "SELECT COUNT(*) FROM images";
        await using var cmd = new MySqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<List<ShimmieImage>> GetImagesAsync(bool skipTrash, CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var sql = @"SELECT id, owner_id, owner_ip, filename, filesize, hash, ext, source,
                           width, height, posted, locked, lossless, video, audio, length, mime,
                           approved, approved_by_id, author, favorites, numeric_score, title, rating, trash
                    FROM images";
        if (skipTrash) sql += " WHERE trash = 0";
        sql += " ORDER BY id";

        var images = new List<ShimmieImage>();
        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            images.Add(new ShimmieImage
            {
                Id = reader.GetInt32("id"),
                OwnerId = reader.GetInt32("owner_id"),
                OwnerIp = reader.GetString("owner_ip"),
                Filename = reader.GetString("filename"),
                Filesize = reader.GetInt32("filesize"),
                Hash = reader.GetString("hash"),
                Ext = reader.GetString("ext"),
                Source = reader.IsDBNull(reader.GetOrdinal("source")) ? null : reader.GetString("source"),
                Width = reader.GetInt32("width"),
                Height = reader.GetInt32("height"),
                Posted = reader.GetDateTime("posted"),
                Locked = reader.GetBoolean("locked"),
                Lossless = reader.IsDBNull(reader.GetOrdinal("lossless")) ? null : reader.GetBoolean("lossless"),
                Video = reader.IsDBNull(reader.GetOrdinal("video")) ? null : reader.GetBoolean("video"),
                Audio = reader.IsDBNull(reader.GetOrdinal("audio")) ? null : reader.GetBoolean("audio"),
                Length = reader.IsDBNull(reader.GetOrdinal("length")) ? null : reader.GetInt32("length"),
                Mime = reader.IsDBNull(reader.GetOrdinal("mime")) ? null : reader.GetString("mime"),
                Approved = reader.GetBoolean("approved"),
                ApprovedById = reader.IsDBNull(reader.GetOrdinal("approved_by_id")) ? null : reader.GetInt32("approved_by_id"),
                Author = reader.IsDBNull(reader.GetOrdinal("author")) ? null : reader.GetString("author"),
                Favorites = reader.GetInt32("favorites"),
                NumericScore = reader.GetInt32("numeric_score"),
                Title = reader.IsDBNull(reader.GetOrdinal("title")) ? null : reader.GetString("title"),
                Rating = reader.GetString("rating"),
                Trash = reader.GetBoolean("trash")
            });
        }

        return images;
    }

    public async Task<List<ShimmieTag>> GetTagsAsync(CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var tags = new List<ShimmieTag>();

        await using var cmd = new MySqlCommand("SELECT id, tag, count FROM tags", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            tags.Add(new ShimmieTag
            {
                Id = reader.GetInt32("id"),
                Tag = reader.GetString("tag"),
                Count = reader.GetInt32("count")
            });
        }

        return tags;
    }

    public async Task<List<ShimmieImageTag>> GetImageTagsAsync(CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var imageTags = new List<ShimmieImageTag>();

        await using var cmd = new MySqlCommand("SELECT image_id, tag_id FROM image_tags", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            imageTags.Add(new ShimmieImageTag
            {
                ImageId = reader.GetInt32("image_id"),
                TagId = reader.GetInt32("tag_id")
            });
        }

        return imageTags;
    }

    public async Task<List<ShimmieComment>> GetCommentsAsync(CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var comments = new List<ShimmieComment>();

        await using var cmd = new MySqlCommand("SELECT id, image_id, owner_id, owner_ip, posted, comment FROM comments ORDER BY id", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            comments.Add(new ShimmieComment
            {
                Id = reader.GetInt32("id"),
                ImageId = reader.GetInt32("image_id"),
                OwnerId = reader.GetInt32("owner_id"),
                OwnerIp = reader.GetString("owner_ip"),
                Posted = reader.GetDateTime("posted"),
                Comment = reader.GetString("comment")
            });
        }

        return comments;
    }

    public async Task<List<ShimmieTagCategory>> GetTagCategoriesAsync(CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var categories = new List<ShimmieTagCategory>();

        try
        {
            await using var cmd = new MySqlCommand("SELECT category, display_singular, display_multiple, color FROM image_tag_categories", conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                categories.Add(new ShimmieTagCategory
                {
                    Category = reader.GetString("category"),
                    DisplaySingular = reader.IsDBNull(reader.GetOrdinal("display_singular")) ? null : reader.GetString("display_singular"),
                    DisplayMultiple = reader.IsDBNull(reader.GetOrdinal("display_multiple")) ? null : reader.GetString("display_multiple"),
                    Color = reader.IsDBNull(reader.GetOrdinal("color")) ? null : reader.GetString("color")
                });
            }
        }
        catch (MySqlException)
        {
            // Table may not exist if tag categories extension isn't installed
        }

        return categories;
    }

    public async Task<List<ShimmieAlias>> GetAliasesAsync(CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var aliases = new List<ShimmieAlias>();

        await using var cmd = new MySqlCommand("SELECT oldtag, newtag FROM aliases", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            aliases.Add(new ShimmieAlias
            {
                OldTag = reader.GetString("oldtag"),
                NewTag = reader.GetString("newtag")
            });
        }

        return aliases;
    }

    public async Task<List<ShimmieAutoTag>> GetAutoTagsAsync(CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var autoTags = new List<ShimmieAutoTag>();

        try
        {
            await using var cmd = new MySqlCommand("SELECT tag, additional_tags FROM auto_tag", conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                autoTags.Add(new ShimmieAutoTag
                {
                    Tag = reader.GetString("tag"),
                    AdditionalTags = reader.GetString("additional_tags")
                });
            }
        }
        catch (MySqlException)
        {
            // Table may not exist if auto_tag extension isn't installed
        }

        return autoTags;
    }

    public async Task<List<ShimmieFavorite>> GetFavoritesAsync(CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var favorites = new List<ShimmieFavorite>();

        try
        {
            await using var cmd = new MySqlCommand("SELECT image_id, user_id, created_at FROM user_favorites", conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                favorites.Add(new ShimmieFavorite
                {
                    ImageId = reader.GetInt32("image_id"),
                    UserId = reader.GetInt32("user_id"),
                    CreatedAt = reader.GetDateTime("created_at")
                });
            }
        }
        catch (MySqlException)
        {
            // Table may not exist if favorites extension isn't installed
        }

        return favorites;
    }

    public async Task<List<ShimmieVote>> GetVotesAsync(CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var votes = new List<ShimmieVote>();

        try
        {
            await using var cmd = new MySqlCommand("SELECT image_id, user_id, score FROM numeric_score_votes", conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                votes.Add(new ShimmieVote
                {
                    ImageId = reader.GetInt32("image_id"),
                    UserId = reader.GetInt32("user_id"),
                    Score = reader.GetInt32("score")
                });
            }
        }
        catch (MySqlException)
        {
            // Table may not exist if numeric score extension isn't installed
        }

        return votes;
    }

    public async Task<List<string>> GetDistinctUserClassesAsync(CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var classes = new List<string>();

        await using var cmd = new MySqlCommand("SELECT DISTINCT class FROM users", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            classes.Add(reader.GetString("class"));
        }

        return classes;
    }

    public async Task<List<ShimmiePM>> GetPrivateMessagesAsync(CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var pms = new List<ShimmiePM>();

        try
        {
            await using var cmd = new MySqlCommand(
                "SELECT id, from_id, from_ip, to_id, sent_date, subject, message, is_read FROM private_message ORDER BY sent_date",
                conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                pms.Add(new ShimmiePM
                {
                    Id = reader.GetInt32("id"),
                    FromId = reader.GetInt32("from_id"),
                    FromIp = reader.GetString("from_ip"),
                    ToId = reader.GetInt32("to_id"),
                    SentDate = reader.GetDateTime("sent_date"),
                    Subject = reader.GetString("subject"),
                    Message = reader.GetString("message"),
                    IsRead = reader.GetBoolean("is_read")
                });
            }
        }
        catch (MySqlException)
        {
            // Table may not exist if PM extension isn't installed
        }

        return pms;
    }

    public async Task<List<ShimmieTagHistory>> GetTagHistoriesAsync(CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var histories = new List<ShimmieTagHistory>();

        try
        {
            await using var cmd = new MySqlCommand(
                "SELECT id, image_id, user_id, user_ip, tags, date_set FROM tag_histories ORDER BY id",
                conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                histories.Add(new ShimmieTagHistory
                {
                    Id = reader.GetInt32("id"),
                    ImageId = reader.GetInt32("image_id"),
                    UserId = reader.GetInt32("user_id"),
                    UserIp = reader.GetString("user_ip"),
                    Tags = reader.GetString("tags"),
                    DateSet = reader.GetDateTime("date_set")
                });
            }
        }
        catch (MySqlException)
        {
            // Table may not exist if tag history extension isn't installed
        }

        return histories;
    }

    public async Task<List<ShimmieSourceHistory>> GetSourceHistoriesAsync(CancellationToken ct = default)
    {
        var conn = await GetConnectionAsync(ct);
        var histories = new List<ShimmieSourceHistory>();

        try
        {
            await using var cmd = new MySqlCommand(
                "SELECT id, image_id, user_id, user_ip, source, date_set FROM source_histories ORDER BY id",
                conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                histories.Add(new ShimmieSourceHistory
                {
                    Id = reader.GetInt32("id"),
                    ImageId = reader.GetInt32("image_id"),
                    UserId = reader.GetInt32("user_id"),
                    UserIp = reader.GetString("user_ip"),
                    Source = reader.GetString("source"),
                    DateSet = reader.GetDateTime("date_set")
                });
            }
        }
        catch (MySqlException)
        {
            // Table may not exist if source history extension isn't installed
        }

        return histories;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
