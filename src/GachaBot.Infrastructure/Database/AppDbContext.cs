using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using GachaBot.Application.Publishing;

namespace GachaBot.Infrastructure.Database;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, string? schema = null) : DbContext(options)
{
    internal string? Schema { get; } = schema;
    public DbSet<ContentRecord> ContentItems => Set<ContentRecord>();

    public DbSet<ContentRevisionRecord> ContentRevisions => Set<ContentRevisionRecord>();

    public DbSet<PublicationRecord> Publications => Set<PublicationRecord>();

    public DbSet<MediaAssetRecord> MediaAssets => Set<MediaAssetRecord>();

    public DbSet<SourceStateRecord> SourceStates => Set<SourceStateRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        if (!string.IsNullOrWhiteSpace(Schema))
        {
            modelBuilder.HasDefaultSchema(Schema);
        }

        modelBuilder.Entity<ContentRecord>(entity =>
        {
            entity.ToTable("ContentItems");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Identity).IsUnique();
            entity.HasIndex(item => new { item.SourceKey, item.ExternalId });
            entity.HasIndex(item => new { item.Status, item.ScheduledAtUtc });
            entity.Property(item => item.Identity).HasMaxLength(768);
            entity.Property(item => item.SourceKey).HasMaxLength(128);
            entity.Property(item => item.ExternalId).HasMaxLength(512);
            entity.Property(item => item.Game).HasConversion<string>().HasMaxLength(64);
            entity.Property(item => item.Kind).HasConversion<string>().HasMaxLength(64);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ArchiveReason).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.Title).HasMaxLength(256);
            entity.Property(item => item.SourceUrl).HasMaxLength(2_048);
            entity.Property(item => item.DocumentHash).HasMaxLength(64);
        });

        modelBuilder.Entity<ContentRevisionRecord>(entity =>
        {
            entity.ToTable("ContentRevisions");
            entity.HasKey(revision => revision.Id);
            entity.HasIndex(revision => new { revision.ContentId, revision.ChangedAtUtc });
            entity.Property(revision => revision.PreviousTitle).HasMaxLength(256);
            entity.Property(revision => revision.PreviousDocumentHash).HasMaxLength(64);
            entity.HasOne(revision => revision.Content)
                .WithMany(content => content.Revisions)
                .HasForeignKey(revision => revision.ContentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaAssetRecord>(entity =>
        {
            entity.ToTable("MediaAssets");
            entity.HasKey(asset => asset.Id);
            entity.HasIndex(asset => new { asset.ContentId, asset.SourceUrl }).IsUnique();
            entity.Property(asset => asset.SourceUrl).HasMaxLength(2_048);
            entity.Property(asset => asset.RelativePath).HasMaxLength(1_024);
            entity.Property(asset => asset.ObjectKey).HasMaxLength(1_024);
            entity.HasIndex(asset => asset.ObjectKey);
            entity.Property(asset => asset.ContentType).HasMaxLength(128);
            entity.Property(asset => asset.Sha256).HasMaxLength(64);
            entity.Property(asset => asset.State).HasConversion<string>().HasMaxLength(32);
            entity.Property(asset => asset.ProcessingNote).HasMaxLength(2_000);
            entity.HasOne(asset => asset.Content)
                .WithMany(content => content.MediaAssets)
                .HasForeignKey(asset => asset.ContentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PublicationRecord>(entity =>
        {
            entity.ToTable("Publications");
            entity.HasKey(publication => publication.Id);
            entity.HasIndex(publication => new { publication.State, publication.DueAtUtc });
            entity.HasIndex(publication => new { publication.DestinationGuildId, publication.DestinationChannelId });
            entity.Property(publication => publication.State).HasConversion<string>().HasMaxLength(32);
            entity.Property(publication => publication.Purpose).HasConversion<string>().HasMaxLength(32)
                .HasDefaultValue(PublicationPurpose.Standard);
            entity.Property(publication => publication.ProviderMessageId).HasMaxLength(2_048);
            entity.Property(publication => publication.LastError).HasMaxLength(2_000);
            entity.HasOne(publication => publication.Content)
                .WithMany(content => content.Publications)
                .HasForeignKey(publication => publication.ContentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SourceStateRecord>(entity =>
        {
            entity.ToTable("SourceStates");
            entity.HasKey(state => state.SourceKey);
            entity.Property(state => state.SourceKey).HasMaxLength(128);
            entity.Property(state => state.ETag).HasMaxLength(512);
            entity.Property(state => state.LastFailureMessage).HasMaxLength(2_000);
        });
    }
}

public sealed class AppDbContextModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is AppDbContext appDbContext
        ? (context.GetType(), appDbContext.Schema, designTime)
        : (context.GetType(), designTime);
}
