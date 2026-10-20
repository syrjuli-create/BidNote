using BidNote.Application.Collecting;
using BidNote.Application.Home;
using BidNote.Domain.Collecting;
using BidNote.Domain.Notices;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Infrastructure.Persistence;

public sealed class NoticeRepository(BidNoteDbContext db) : INoticeRepository, IHomeQueries
{
    public Task<Notice?> FindAsync(string sourceSystem, BusinessType businessType, string bidNo, CancellationToken cancellationToken) =>
        db.Notices.Include(x => x.Histories).Include(x => x.Licenses).AsSplitQuery()
            .FirstOrDefaultAsync(x => x.SourceSystem == sourceSystem && x.BusinessType == businessType && x.BidNo == bidNo, cancellationToken);

    public void Add(Notice notice) => db.Notices.Add(notice);

    public void AddRaw(ApiRawResponse raw) => db.ApiRawResponses.Add(raw);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<HomeSummary> GetSummaryAsync(DateTime now, CancellationToken cancellationToken)
    {
        var open = db.Notices.AsNoTracking().Where(x => x.IsActive && x.DueAt > now);
        DateTime soonLimit = HomeRules.ClosingSoonLimit(now);
        DateTime newSince = now - HomeRules.NewWindow;

        int active = await open.CountAsync(cancellationToken).ConfigureAwait(false);
        int closingSoonCount = await open.CountAsync(x => x.DueAt < soonLimit, cancellationToken).ConfigureAwait(false);
        int newCount = await open.CountAsync(x => x.PostedAt >= newSince, cancellationToken).ConfigureAwait(false);
        int correctedCount = await open.CountAsync(x => x.CorrectionCount > 0, cancellationToken).ConfigureAwait(false);

        var closingSoon = await ToItems(open.Where(x => x.DueAt < soonLimit).OrderBy(x => x.DueAt), cancellationToken).ConfigureAwait(false);
        var corrected = await ToItems(open.Where(x => x.CorrectionCount > 0).OrderByDescending(x => x.LastModifiedAt), cancellationToken).ConfigureAwait(false);

        DateTime? lastCollected = await db.Notices.AsNoTracking().OrderByDescending(x => x.LastCheckedAt)
            .Select(x => (DateTime?)x.LastCheckedAt).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return new HomeSummary(active, closingSoonCount, newCount, correctedCount, closingSoon, corrected, lastCollected);
    }

    public async Task<NoticeSearchResult> SearchAsync(NoticeFilter filter, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var open = db.Notices.AsNoTracking().Where(x => x.IsActive && x.DueAt > now);

        var q = open;
        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            string k = filter.Keyword.Trim();
            q = q.Where(x => x.Name.Contains(k) || x.Agency.Contains(k) || x.DemandAgency.Contains(k));
        }
        if (filter.Regions.Count > 0) q = q.Where(x => filter.Regions.Contains(x.Region));
        if (!string.IsNullOrWhiteSpace(filter.License))
        {
            // 면허 제한 중 하나라도 맞으면 보여 준다. 면허 정보가 없는 공고는 대표 업종으로 비교한다.
            q = q.Where(x => x.Licenses.Any(l => l.Name == filter.License) || (!x.Licenses.Any() && x.License == filter.License));
        }
        q = filter.Amount switch
        {
            AmountRange.Under100M => q.Where(x => x.BasePrice < 100_000_000),
            AmountRange.From100MTo500M => q.Where(x => x.BasePrice >= 100_000_000 && x.BasePrice < 500_000_000),
            AmountRange.Over500M => q.Where(x => x.BasePrice >= 500_000_000),
            _ => q,
        };
        if (filter.DueWithinDays is { } days)
        {
            DateTime limit = now.Date.AddDays(days + 1); // "2일 이내" = 모레 자정 전까지
            q = q.Where(x => x.DueAt < limit);
        }
        if (filter.CorrectedOnly) q = q.Where(x => x.CorrectionCount > 0);
        if (!string.IsNullOrWhiteSpace(filter.ContractMethod)) q = q.Where(x => x.ContractMethod == filter.ContractMethod);
        if (filter.ExcludeNegotiated) q = q.Where(x => x.ContractMethod != ContractMethods.Negotiated);
        if (filter.NewOnly)
        {
            DateTime since = now - HomeRules.NewWindow;
            q = q.Where(x => x.PostedAt >= since);
        }
        q = filter.Sort switch
        {
            NoticeSort.AmountHigh => q.OrderByDescending(x => x.BasePrice).ThenBy(x => x.DueAt),
            NoticeSort.Newest => q.OrderByDescending(x => x.PostedAt).ThenBy(x => x.DueAt),
            _ => q.OrderBy(x => x.DueAt).ThenBy(x => x.Id),
        };

        int size = Math.Clamp(filter.PageSize, 1, HomeRules.MaxPageSize);
        int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);
        int pageCount = Math.Max(1, (total + size - 1) / size);
        int page = Math.Clamp(filter.Page, 1, pageCount); // 범위를 넘는 페이지는 마지막 페이지로
        var items = await Project(q.Skip((page - 1) * size).Take(size)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var regions = await Facet(open.GroupBy(x => x.Region), cancellationToken).ConfigureAwait(false);
        var licenses = await LicenseFacet(open, cancellationToken).ConfigureAwait(false);
        var contracts = await Facet(open.GroupBy(x => x.ContractMethod), cancellationToken).ConfigureAwait(false);
        return new NoticeSearchResult(items, regions, licenses) { TotalCount = total, Page = page, PageSize = size, ContractMethods = contracts };
    }

    private static async Task<List<FacetCount>> Facet(IQueryable<IGrouping<string, Notice>> groups, CancellationToken cancellationToken)
    {
        var rows = await groups.Select(g => new FacetCount(g.Key, g.Count())).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Where(x => x.Value.Length > 0).OrderByDescending(x => x.Count).ThenBy(x => x.Value, StringComparer.Ordinal)];
    }

    private async Task<List<FacetCount>> LicenseFacet(IQueryable<Notice> open, CancellationToken cancellationToken)
    {
        // 면허 제한이 있는 공고는 면허별로, 없는 공고는 대표 업종으로 센다.
        var fromLicenses = await db.NoticeLicenses.AsNoTracking().Where(l => open.Any(n => n.Id == l.NoticeId))
            .Select(l => new { l.NoticeId, l.Name }).Distinct()
            .GroupBy(l => l.Name).Select(g => new FacetCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var fromMain = await open.Where(n => !n.Licenses.Any()).GroupBy(n => n.License).Select(g => new FacetCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. fromLicenses.Concat(fromMain).GroupBy(f => f.Value).Select(g => new FacetCount(g.Key, g.Sum(f => f.Count)))
            .Where(x => x.Value.Length > 0).OrderByDescending(x => x.Count).ThenBy(x => x.Value, StringComparer.Ordinal)];
    }

    private static IQueryable<NoticeListItem> Project(IQueryable<Notice> query) =>
        query.Select(x => new NoticeListItem(x.Id, x.BidNo, x.Name, x.Agency, x.Region, x.License, x.BasePrice, x.PostedAt, x.DueAt, x.CorrectionCount));

    private static Task<List<NoticeListItem>> ToItems(IQueryable<Notice> query, CancellationToken cancellationToken) =>
        Project(query.Take(HomeRules.ListSize)).ToListAsync(cancellationToken);
}
