using Microsoft.EntityFrameworkCore;

namespace Nuuru.Tools.ShimmieMigration.Source;

public class ShimmieDbContext : DbContext
{
    public ShimmieDbContext(DbContextOptions<ShimmieDbContext> options) : base(options)
    {
    }

    public DbSet<ShimmieUser> Users => Set<ShimmieUser>();
    public DbSet<ShimmieImage> Images => Set<ShimmieImage>();
    public DbSet<ShimmieTag> Tags => Set<ShimmieTag>();
    public DbSet<ShimmieImageTag> ImageTags => Set<ShimmieImageTag>();
    public DbSet<ShimmieComment> Comments => Set<ShimmieComment>();
    public DbSet<ShimmieTagCategory> TagCategories => Set<ShimmieTagCategory>();
    public DbSet<ShimmieAlias> Aliases => Set<ShimmieAlias>();
    public DbSet<ShimmieAutoTag> AutoTags => Set<ShimmieAutoTag>();
    public DbSet<ShimmieFavorite> Favorites => Set<ShimmieFavorite>();
    public DbSet<ShimmieVote> Votes => Set<ShimmieVote>();
    public DbSet<ShimmiePM> PrivateMessages => Set<ShimmiePM>();
    public DbSet<ShimmieTagHistory> TagHistories => Set<ShimmieTagHistory>();
    public DbSet<ShimmieSourceHistory> SourceHistories => Set<ShimmieSourceHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ImageTag composite key
        modelBuilder.Entity<ShimmieImageTag>()
            .HasKey(it => new { it.ImageId, it.TagId });

        // Favorite composite key
        modelBuilder.Entity<ShimmieFavorite>()
            .HasKey(f => new { f.ImageId, f.UserId });

        // Vote composite key
        modelBuilder.Entity<ShimmieVote>()
            .HasKey(v => new { v.ImageId, v.UserId });

        // Configure relationships
        modelBuilder.Entity<ShimmieImage>()
            .HasOne(i => i.Owner)
            .WithMany()
            .HasForeignKey(i => i.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ShimmieComment>()
            .HasOne(c => c.Image)
            .WithMany()
            .HasForeignKey(c => c.ImageId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ShimmieComment>()
            .HasOne(c => c.Owner)
            .WithMany()
            .HasForeignKey(c => c.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
