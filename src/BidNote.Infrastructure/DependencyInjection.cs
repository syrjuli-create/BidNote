using BidNote.Application.Collecting;
using BidNote.Application.Home;
using BidNote.Infrastructure.G2B;
using BidNote.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BidNote.Infrastructure;

public static class DependencyInjection
{
    /// <summary>SQLite 저장소와 수집기를 등록한다. 상대 경로 DB 위치는 <paramref name="basePath"/> 기준이다.</summary>
    public static IServiceCollection AddBidNoteInfrastructure(this IServiceCollection services, IConfiguration configuration, string basePath)
    {
        string dbPath = configuration["Storage:DatabasePath"] ?? Path.Combine("data", "bidnote.db");
        if (!Path.IsPathRooted(dbPath)) dbPath = Path.Combine(basePath, dbPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        // 일반 요청은 요청마다 하나의 DbContext를, 화면 버튼처럼 동시에 여러 번 불릴 수 있는 곳은 팩토리로 매번 새로 만든다.
        services.AddDbContextFactory<BidNoteDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<BidNoteDbContext>>().CreateDbContext());
        services.AddScoped<BidNote.Application.Favorites.IFavoriteStore, FavoriteStore>();
        services.AddScoped<BidNote.Application.Bidding.IMyBidStore, MyBidStore>();
        services.AddSingleton(new StorageLocation(dbPath));
        services.AddScoped<ICollectStatsQueries, CollectStatsQueries>();
        services.AddScoped<BidNote.Application.Notices.INoticeDetailQueries, NoticeDetailQueries>();
        services.AddScoped<IApiUsageTracker, ApiUsageTracker>();
        services.AddScoped<ICollectorSettingsStore, CollectorSettingsStore>();
        services.AddOptions<CollectorOptions>().Bind(configuration.GetSection(CollectorOptions.SectionName));
        services.AddOptions<ApiLimitOptions>().Bind(configuration.GetSection(ApiLimitOptions.SectionName));
        services.AddScoped<NoticeRepository>();
        services.AddScoped<INoticeRepository>(sp => sp.GetRequiredService<NoticeRepository>());
        services.AddScoped<IHomeQueries>(sp => sp.GetRequiredService<NoticeRepository>());
        services.AddScoped<CollectService>();
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<G2BOptions>().Bind(configuration.GetSection(G2BOptions.SectionName));
        var g2b = configuration.GetSection(G2BOptions.SectionName).Get<G2BOptions>() ?? new G2BOptions();
        if (g2b.UseFakeData)
        {
            services.AddScoped<IBidCollector, FakeG2BCollector>();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(g2b.ServiceKey) || g2b.ServiceKey.StartsWith("FAKE", StringComparison.Ordinal))
                throw new InvalidOperationException("실제 모드(G2B:UseFakeData=false)에는 공공데이터포털 인증키가 필요합니다. appsettings.Local.json의 G2B:ServiceKey를 확인하세요.");
            services.AddHttpClient<G2BApiClient>(c =>
            {
                c.BaseAddress = new Uri(g2b.BaseUrl);
                c.Timeout = TimeSpan.FromSeconds(60);
            });
            services.AddScoped<IBidCollector, G2BCollector>();
        }

        return services;
    }
}
