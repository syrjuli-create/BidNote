using BidNote.Domain.Bidding;
using BidNote.Domain.Favorites;
using BidNote.Domain.Collecting;
using BidNote.Domain.Notices;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Infrastructure.Persistence;

public sealed class BidNoteDbContext(DbContextOptions<BidNoteDbContext> options) : DbContext(options)
{
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<NoticeHistory> NoticeHistories => Set<NoticeHistory>();
    public DbSet<ApiRawResponse> ApiRawResponses => Set<ApiRawResponse>();
    public DbSet<ApiUsage> ApiUsages => Set<ApiUsage>();
    public DbSet<NoticeLicense> NoticeLicenses => Set<NoticeLicense>();
    public DbSet<CollectorSetting> CollectorSettings => Set<CollectorSetting>();
    public DbSet<FavoriteNotice> FavoriteNotices => Set<FavoriteNotice>();
    public DbSet<MyBid> MyBids => Set<MyBid>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notice>(e =>
        {
            e.HasIndex(x => new { x.SourceSystem, x.BusinessType, x.BidNo }).IsUnique();
            e.HasIndex(x => x.DueAt);
            e.HasIndex(x => x.PostedAt);
            e.Property(x => x.SourceSystem).HasMaxLength(10);
            e.Property(x => x.BidNo).HasMaxLength(40);
            e.Property(x => x.BidSeq).HasMaxLength(10);
            e.Property(x => x.RawHash).HasMaxLength(64);
            e.HasMany(x => x.Histories).WithOne(x => x.Notice).HasForeignKey(x => x.NoticeId);
            e.HasMany(x => x.Licenses).WithOne(x => x.Notice).HasForeignKey(x => x.NoticeId);
        });
        modelBuilder.Entity<NoticeHistory>(e => e.HasIndex(x => new { x.NoticeId, x.DetectedAt }));
        modelBuilder.Entity<NoticeLicense>(e =>
        {
            e.HasIndex(x => x.Name);
            e.Property(x => x.Name).HasMaxLength(100);
        });
        modelBuilder.Entity<ApiRawResponse>(e =>
        {
            e.HasIndex(x => new { x.SourceSystem, x.BusinessType, x.BidNo });
            e.HasIndex(x => x.CollectedAt);
        });
        modelBuilder.Entity<CollectorSetting>(e => e.Property(x => x.Id).ValueGeneratedNever());
        modelBuilder.Entity<FavoriteNotice>(e =>
        {
            e.HasIndex(x => x.NoticeId).IsUnique();
            e.Property(x => x.Memo).HasMaxLength(FavoriteNotice.MaxMemoLength);
            e.HasOne(x => x.Notice).WithMany().HasForeignKey(x => x.NoticeId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MyBid>(e =>
        {
            e.HasIndex(x => x.NoticeId).IsUnique();
            e.Property(x => x.Memo).HasMaxLength(MyBid.MaxMemoLength);
            e.HasOne(x => x.Notice).WithMany().HasForeignKey(x => x.NoticeId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ApiUsage>(e =>
        {
            e.HasIndex(x => new { x.Date, x.ApiName }).IsUnique();
            e.Property(x => x.ApiName).HasMaxLength(100);
        });
    }
}
