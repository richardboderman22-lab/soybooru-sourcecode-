using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nuuru.Server.Data;
using Nuuru.Server.Models;
using Nuuru.Server.Services;
using Nuuru.Server.Services.Storage;
using Nuuru.Tools.ShimmieMigration;
using Nuuru.Tools.ShimmieMigration.Source;
using Spectre.Console;

AnsiConsole.Write(new FigletText("Shimmie Migration").Color(Color.Cyan1));
AnsiConsole.WriteLine();

// Get configuration from user
var options = await GetMigrationOptionsAsync();

if (options == null)
{
    AnsiConsole.MarkupLine("[red]Migration cancelled.[/]");
    return 1;
}

// Setup services
var services = new ServiceCollection();

// Configure Nuuru DbContext
switch (options.NuuruProvider)
{
    case DatabaseProvider.PostgreSQL:
        services.AddDbContext<PostgresApplicationDbContext>(opt =>
            opt.UseNpgsql(options.NuuruConnectionString));
        services.AddScoped<ApplicationDbContext>(sp =>
            sp.GetRequiredService<PostgresApplicationDbContext>());
        break;
    case DatabaseProvider.SQLite:
        services.AddDbContext<ApplicationDbContext>(opt =>
            opt.UseSqlite(options.NuuruConnectionString));
        break;
    case DatabaseProvider.MySQL:
        AnsiConsole.MarkupLine("[red]MySQL is not supported as Nuuru target (use PostgreSQL or SQLite)[/]");
        Environment.Exit(1);
        break;
}

// Configure Shimmie data source
if (options.ShimmieProvider == DatabaseProvider.MySQL)
{
    // Use raw ADO.NET for MySQL
    services.AddSingleton<IShimmieDataSource>(sp => new MySqlShimmieDataReader(options.ShimmieConnectionString));
}
else
{
    // Use EF Core for PostgreSQL/SQLite
    services.AddDbContext<ShimmieDbContext>(opt =>
    {
        switch (options.ShimmieProvider)
        {
            case DatabaseProvider.PostgreSQL:
                opt.UseNpgsql(options.ShimmieConnectionString);
                break;
            case DatabaseProvider.SQLite:
                opt.UseSqlite(options.ShimmieConnectionString);
                break;
        }
    });
    services.AddScoped<IShimmieDataSource, EfCoreShimmieDataSource>();
}

// Configure Identity
services.AddIdentityCore<ApplicationUser>(opt =>
{
    opt.User.RequireUniqueEmail = false;
    opt.Password.RequireDigit = false;
    opt.Password.RequireLowercase = false;
    opt.Password.RequireUppercase = false;
    opt.Password.RequireNonAlphanumeric = false;
    opt.Password.RequiredLength = 1;
})
.AddRoles<ApplicationRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

// Build configuration for file storage and thumbnail services
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["FileStorage:Path"] = Path.GetFullPath(options.NuuruUploadsPath)
    })
    .Build();
services.AddSingleton<IConfiguration>(configuration);
services.AddLogging();

services.AddSingleton(options);
services.AddScoped<IFileStorageService, LocalFileStorageService>();
services.AddScoped<MigrationService>();

var serviceProvider = services.BuildServiceProvider();

// Test connections
AnsiConsole.MarkupLine("[yellow]Testing database connections...[/]");

try
{
    await using var scope = serviceProvider.CreateAsyncScope();

    // Test Shimmie connection
    if (options.ShimmieProvider == DatabaseProvider.MySQL)
    {
        var mysqlReader = scope.ServiceProvider.GetRequiredService<IShimmieDataSource>() as MySqlShimmieDataReader;
        await mysqlReader!.GetConnectionAsync();
        AnsiConsole.MarkupLine("[green]Shimmie database (MySQL): Connected[/]");
    }
    else
    {
        var shimmieDb = scope.ServiceProvider.GetRequiredService<ShimmieDbContext>();
        await shimmieDb.Database.CanConnectAsync();
        AnsiConsole.MarkupLine($"[green]Shimmie database ({options.ShimmieProvider}): Connected[/]");
    }

    var nuuruDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await nuuruDb.Database.CanConnectAsync();
    AnsiConsole.MarkupLine($"[green]Nuuru database ({options.NuuruProvider}): Connected[/]");
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Database connection failed: {ex.Message}[/]");
    return 1;
}

// Select migration mode
if (!Console.IsInputRedirected)
{
    var mode = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Migration mode:")
            .AddChoices("Full Migration", "Sync Tags Only"));
    options.SyncTagsOnly = mode == "Sync Tags Only";
}

// Show summary and confirm
AnsiConsole.WriteLine();
var table = new Table();
table.AddColumn("Setting");
table.AddColumn("Value");
table.AddRow("Mode", options.SyncTagsOnly ? "[cyan]Sync Tags Only[/]" : "[green]Full Migration[/]");
table.AddRow("Shimmie Provider", options.ShimmieProvider.ToString());
table.AddRow("Nuuru Provider", options.NuuruProvider.ToString());
table.AddRow("Shimmie Images", options.ShimmieImagesPath);
table.AddRow("Shimmie Thumbs", options.ShimmieThumbsPath);
table.AddRow("Nuuru Uploads", options.NuuruUploadsPath);
table.AddRow("Copy Files", options.CopyFiles ? "Yes" : "No");
table.AddRow("Skip Trash", options.SkipTrash ? "Yes" : "No");
table.AddRow("Preserve Post IDs", options.PreservePostIds ? "Yes" : "No");
table.AddRow("Fetch Gravatars", options.FetchGravatarAvatars ? "Yes" : "No");
table.AddRow("Batch Size", options.BatchSize.ToString());
AnsiConsole.Write(table);
AnsiConsole.WriteLine();

if (!Console.IsInputRedirected)
{
    var confirmMessage = options.SyncTagsOnly ? "Start tag sync?" : "Start migration?";
    if (!AnsiConsole.Confirm(confirmMessage))
    {
        AnsiConsole.MarkupLine("[yellow]Operation cancelled.[/]");
        return 0;
    }
}
else
{
    AnsiConsole.MarkupLine("[green]Auto-starting (non-interactive mode)[/]");
}

// Run migration or tag sync
try
{
    await using var scope = serviceProvider.CreateAsyncScope();
    var migrationService = scope.ServiceProvider.GetRequiredService<MigrationService>();

    if (options.SyncTagsOnly)
        await migrationService.RunTagSyncAsync();
    else
        await migrationService.RunMigrationAsync();

    return 0;
}
catch (Exception ex)
{
    try { AnsiConsole.WriteException(ex); }
    catch { Console.Error.WriteLine(ex); }
    return 1;
}

static async Task<MigrationOptions?> GetMigrationOptionsAsync()
{
    // Check for config file first - auto-use if exists and console is not interactive
    var configPath = Path.Combine(AppContext.BaseDirectory, "migration-config.json");
    if (File.Exists(configPath))
    {
        bool useConfig = true;
        if (!Console.IsInputRedirected)
        {
            useConfig = AnsiConsole.Confirm($"Found config file at {configPath}. Use it?", true);
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]Using config file: {configPath}[/]");
        }

        if (useConfig)
        {
            var json = await File.ReadAllTextAsync(configPath);
            var loadedOptions = System.Text.Json.JsonSerializer.Deserialize<MigrationOptions>(json);
            if (loadedOptions != null) return loadedOptions;
        }
    }

    // Interactive configuration
    AnsiConsole.MarkupLine("[bold]Configure migration settings:[/]");
    AnsiConsole.WriteLine();

    var shimmieProvider = AnsiConsole.Prompt(
        new SelectionPrompt<DatabaseProvider>()
            .Title("Shimmie database provider:")
            .AddChoices(DatabaseProvider.MySQL, DatabaseProvider.PostgreSQL, DatabaseProvider.SQLite));

    var shimmieConnStr = AnsiConsole.Prompt(
        new TextPrompt<string>("Shimmie connection string:")
            .DefaultValue(GetDefaultConnectionString(shimmieProvider, "shimmie")));

    var nuuruProvider = AnsiConsole.Prompt(
        new SelectionPrompt<DatabaseProvider>()
            .Title("Nuuru database provider:")
            .AddChoices(DatabaseProvider.PostgreSQL, DatabaseProvider.SQLite));

    var nuuruConnStr = AnsiConsole.Prompt(
        new TextPrompt<string>("Nuuru connection string:")
            .DefaultValue(GetDefaultConnectionString(nuuruProvider, "nuuru")));

    var shimmieImagesPath = AnsiConsole.Prompt(
        new TextPrompt<string>("Shimmie images directory:")
            .DefaultValue("/var/www/shimmie/images"));

    var shimmieThumbsPath = AnsiConsole.Prompt(
        new TextPrompt<string>("Shimmie thumbs directory:")
            .DefaultValue("/var/www/shimmie/thumbs"));

    var nuuruUploadsPath = AnsiConsole.Prompt(
        new TextPrompt<string>("Nuuru uploads directory:")
            .DefaultValue("./uploads"));

    var copyFiles = AnsiConsole.Confirm("Copy files to Nuuru uploads directory?", true);
    var skipTrash = AnsiConsole.Confirm("Skip trashed posts?", true);
    var preserveIds = AnsiConsole.Confirm("Preserve original post IDs?", true);
    var fetchGravatars = AnsiConsole.Confirm("Fetch Gravatar avatars for users with emails?", true);

    var batchSize = AnsiConsole.Prompt(
        new TextPrompt<int>("Batch size for database operations:")
            .DefaultValue(1000));

    var parallelism = AnsiConsole.Prompt(
        new TextPrompt<int>("File processing parallelism (concurrent files):")
            .DefaultValue(4));

    var migrationOptions = new MigrationOptions
    {
        ShimmieConnectionString = shimmieConnStr,
        NuuruConnectionString = nuuruConnStr,
        ShimmieProvider = shimmieProvider,
        NuuruProvider = nuuruProvider,
        ShimmieImagesPath = shimmieImagesPath,
        ShimmieThumbsPath = shimmieThumbsPath,
        NuuruUploadsPath = nuuruUploadsPath,
        CopyFiles = copyFiles,
        SkipTrash = skipTrash,
        PreservePostIds = preserveIds,
        FetchGravatarAvatars = fetchGravatars,
        BatchSize = batchSize,
        Parallelism = parallelism
    };

    // Offer to save config
    if (AnsiConsole.Confirm("Save configuration for future use?", false))
    {
        var json = System.Text.Json.JsonSerializer.Serialize(migrationOptions, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(configPath, json);
        AnsiConsole.MarkupLine($"[green]Configuration saved to {configPath}[/]");
    }

    return migrationOptions;
}

static string GetDefaultConnectionString(DatabaseProvider provider, string dbName)
{
    return provider switch
    {
        DatabaseProvider.MySQL => $"Server=localhost;Database={dbName};User=root;Password=;",
        DatabaseProvider.PostgreSQL => $"Host=localhost;Database={dbName};Username=postgres;Password=;",
        DatabaseProvider.SQLite => $"Data Source={dbName}.db",
        _ => ""
    };
}
