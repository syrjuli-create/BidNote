using BidNote.Domain.Bidding;
using BidNote.Domain.Company;
using BidNote.Domain.Favorites;
using BidNote.Domain.Collecting;
using BidNote.Domain.Notices;
using BidNote.Domain.Results;
using BidNote.Domain.Searching;
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
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<LastSearch> LastSearches => Set<LastSearch>();
    public DbSet<BidResult> BidResults => Set<BidResult>();
    public DbSet<BidResultRank> BidResultRanks => Set<BidResultRank>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<CompanyLicense> CompanyLicenses => Set<CompanyLicense>();
    public DbSet<CompanyLocation> CompanyLocations => Set<CompanyLocation>();
    public DbSet<NoticeRegionLimit> NoticeRegionLimits => Set<NoticeRegionLimit>();

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
            e.HasMany(x => x.RegionLimits).WithOne(x => x.Notice).HasForeignKey(x => x.NoticeId).OnDelete(DeleteBehavior.Cascade);
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
        modelBuilder.Entity<SavedSearch>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(SavedSearch.MaxNameLength);
            e.Property(x => x.Query).HasMaxLength(SavedSearch.MaxQueryLength);
        });
        modelBuilder.Entity<BidResult>(e =>
        {
            e.HasIndex(x => x.NoticeId).IsUnique();
            e.Property(x => x.WinnerName).HasMaxLength(BidResultRank.MaxNameLength);
            e.HasOne(x => x.Notice).WithMany().HasForeignKey(x => x.NoticeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Ranks).WithOne().HasForeignKey(x => x.BidResultId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.IsOpened);
        });
        modelBuilder.Entity<BidResultRank>(e =>
        {
            e.HasIndex(x => new { x.BidResultId, x.Rank });
            e.HasIndex(x => x.BizNo); // 경쟁사 성향 (업체별 모아 보기)
            e.Property(x => x.CompanyName).HasMaxLength(BidResultRank.MaxNameLength);
        });
        modelBuilder.Entity<CompanyProfile>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(CompanyProfile.MaxNameLength);
            e.Property(x => x.BizNo).HasMaxLength(CompanyProfile.MaxBizNoLength);
            e.Property(x => x.Email).HasMaxLength(CompanyProfile.MaxEmailLength);
        });
        modelBuilder.Entity<CompanyLocation>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(CompanyLocation.MaxNameLength);
        });
        modelBuilder.Entity<NoticeRegionLimit>(e =>
        {
            e.HasIndex(x => x.NoticeId);
            e.Property(x => x.Name).HasMaxLength(NoticeRegionLimit.MaxNameLength);
        });
        modelBuilder.Entity<CompanyLicense>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(CompanyLicense.MaxNameLength);
            e.Ignore(x => x.AmountLimit);
            e.Ignore(x => x.MainFieldNames);
        });
        modelBuilder.Entity<NoticeLicense>(e =>
        {
            e.Ignore(x => x.AlternativeNames);
            e.Ignore(x => x.MainFieldGroups);
        });
        modelBuilder.Entity<LastSearch>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Query).HasMaxLength(SavedSearch.MaxQueryLength);
        });
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
