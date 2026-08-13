using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Nuuru.Server.Data;
using Nuuru.Server.Services;
using System.IO;

namespace Nuuru.Server.Tests.Helpers;

public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    public string? DatabaseName { get; set; }
    private SqliteConnection? _connection;
    private string? _dataProtectionKeyDirectory;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Set Testing environment - this will load appsettings.Testing.json (which uses SQLite)
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext configuration that was set up by Program.cs
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));

            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Create and open a SQLite connection that stays open for the lifetime of the factory
            // This is necessary because in-memory SQLite databases are destroyed when the connection closes
            var dbName = DatabaseName ?? Guid.NewGuid().ToString();
            _connection = new SqliteConnection($"DataSource={dbName};Mode=Memory;Cache=Shared");
            _connection.Open();

            // Keep data protection isolated from the machine/user profile so tests don't load
            // encrypted keys that are unavailable inside the sandboxed environment.
            _dataProtectionKeyDirectory = Path.Combine(AppContext.BaseDirectory, "test-dpkeys", dbName);
            Directory.CreateDirectory(_dataProtectionKeyDirectory);
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(_dataProtectionKeyDirectory))
                .SetApplicationName("Nuuru.Server.Tests");

            // Add DbContext with our managed SQLite connection
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            // Replace captcha service with NoOp for testing
            var captchaDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ICaptchaService));

            if (captchaDescriptor != null)
            {
                services.Remove(captchaDescriptor);
            }

            services.AddScoped<ICaptchaService, NoOpCaptchaService>();
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.BaseAddress = new Uri("https://localhost");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Dispose();

            if (_dataProtectionKeyDirectory is not null && Directory.Exists(_dataProtectionKeyDirectory))
            {
                Directory.Delete(_dataProtectionKeyDirectory, recursive: true);
            }
        }
    }
}
