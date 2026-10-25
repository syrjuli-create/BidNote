using BidNote.Application.Company;
using BidNote.Domain.Company;
using BidNote.Domain.Notices;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Infrastructure.Persistence;

/// <summary>회사·면허 정보 저장과 면허·금액으로 참여 가능한 공고 판정. 화면 버튼에서 불리므로 호출마다 DB 연결을 새로 연다.</summary>
public sealed class CompanyStore(IDbContextFactory<BidNoteDbContext> factory, TimeProvider timeProvider) : ICompanyStore
{
    public async Task<CompanyInfo> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var p = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == CompanyProfile.SingletonId, cancellationToken).ConfigureAwait(false);
        var licenses = await db.CompanyLicenses.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var locations = await db.CompanyLocations.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return new CompanyInfo(p?.Name ?? "", p?.BizNo ?? "", p?.Region ?? "", p?.Email ?? "",
            [.. licenses.OrderBy(x => x.Name, StringComparer.CurrentCulture)
                .Select(x => new CompanyLicenseInfo(x.Name, x.CapacityAmount, x.PerformanceAmount, x.PerformanceMultiple, x.AmountLimit) { MainFields = x.MainFieldNames })])
        {
            Locations = [.. locations.OrderByDescending(x => x.IsHeadOffice).ThenBy(x => x.Name, StringComparer.CurrentCulture)
                .Select(x => new CompanyLocationInfo(x.Name, x.IsHeadOffice))],
        };
    }

    public async Task<bool> SaveLicenseMainFieldsAsync(string name, IEnumerable<string> mainFields, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mainFields);
        string text = (name ?? "").Trim();
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.CompanyLicenses.FirstOrDefaultAsync(x => x.Name == text, cancellationToken).ConfigureAwait(false);
        if (row is null) return false;
        row.MainFields = NoticeLicense.JoinAlternatives(mainFields);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> MainFieldOptionsAsync(DateTime now, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await OpenLicenseRowsAsync(db, now, cancellationToken).ConfigureAwait(false);
        return rows.Where(r => r.Row.MainFieldGroups.Count > 0)
            .GroupBy(r => r.Row.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.SelectMany(r => r.Row.MainFieldGroups.SelectMany(x => x))
                .GroupBy(x => x, StringComparer.Ordinal).OrderByDescending(x => x.Count()).Select(x => x.Key)], StringComparer.Ordinal);
    }

    public async Task<bool> AddLocationAsync(string name, bool isHeadOffice, CancellationToken cancellationToken)
    {
        // 공백이 여러 칸이어도 나라장터 이름과 맞게 한 칸으로 줄인다.
        string text = Cut(string.Join(' ', (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)), CompanyLocation.MaxNameLength);
        if (text.Length == 0) return false;
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (await db.CompanyLocations.AnyAsync(x => x.Name == text, cancellationToken).ConfigureAwait(false)) return false;
        if (isHeadOffice)
            await db.CompanyLocations.Where(x => x.IsHeadOffice).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsHeadOffice, false), cancellationToken).ConfigureAwait(false);
        db.CompanyLocations.Add(new CompanyLocation { Name = text, IsHeadOffice = isHeadOffice, AddedAt = timeProvider.GetLocalNow().DateTime });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task RemoveLocationAsync(string name, CancellationToken cancellationToken)
    {
        string text = (name ?? "").Trim();
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.CompanyLocations.Where(x => x.Name == text).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LocationOption>> LocationOptionsAsync(DateTime now, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.NoticeRegionLimits.AsNoTracking().Where(r => r.Notice!.IsActive && r.Notice.DueAt > now)
            .GroupBy(r => r.Name).Select(g => new LocationOption(g.Key, g.Select(r => r.NoticeId).Distinct().Count()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.OrderByDescending(x => x.OpenNoticeCount).ThenBy(x => x.Name, StringComparer.CurrentCulture)];
    }

    public async Task SaveProfileAsync(CompanyProfileInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var p = await db.CompanyProfiles.FirstOrDefaultAsync(x => x.Id == CompanyProfile.SingletonId, cancellationToken).ConfigureAwait(false);
        if (p is null)
        {
            p = new CompanyProfile();
            db.CompanyProfiles.Add(p);
        }
        p.Name = Cut(input.Name, CompanyProfile.MaxNameLength);
        p.BizNo = Cut(input.BizNo, CompanyProfile.MaxBizNoLength);
        p.Region = Cut(input.Region, 20);
        p.Email = Cut(input.Email, CompanyProfile.MaxEmailLength);
        p.UpdatedAt = timeProvider.GetLocalNow().DateTime;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> AddLicenseAsync(string name, CancellationToken cancellationToken)
    {
        string text = Cut(name, CompanyLicense.MaxNameLength);
        if (text.Length == 0) return false;
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (await db.CompanyLicenses.AnyAsync(x => x.Name == text, cancellationToken).ConfigureAwait(false)) return false;
        db.CompanyLicenses.Add(new CompanyLicense { Name = text, AddedAt = timeProvider.GetLocalNow().DateTime });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task RemoveLicenseAsync(string name, CancellationToken cancellationToken)
    {
        string text = (name ?? "").Trim();
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.CompanyLicenses.Where(x => x.Name == text).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SaveLicenseAmountsAsync(string name, LicenseAmountsInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        string text = (name ?? "").Trim();
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.CompanyLicenses.FirstOrDefaultAsync(x => x.Name == text, cancellationToken).ConfigureAwait(false);
        if (row is null) return false;
        row.CapacityAmount = input.CapacityAmount is > 0 ? input.CapacityAmount : null;
        row.PerformanceAmount = input.PerformanceAmount is > 0 ? input.PerformanceAmount : null;
        row.PerformanceMultiple = input.PerformanceMultiple is > 0 ? input.PerformanceMultiple : null;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<LicenseOption>> LicenseOptionsAsync(DateTime now, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await OpenLicenseRowsAsync(db, now, cancellationToken).ConfigureAwait(false);
        return [.. rows
            .SelectMany(r => r.Row.Alternatives.Prepend(r.Row.Name).Select(name => (r.NoticeId, Name: name)))
            .Distinct()
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .Select(g => new LicenseOption(g.Key, g.Count()))
            .OrderByDescending(x => x.OpenNoticeCount).ThenBy(x => x.Name, StringComparer.CurrentCulture)];
    }

    public async Task<EligibilitySummary> SummaryAsync(DateTime now, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var mine = await LicensesAsync(db, cancellationToken).ConfigureAwait(false);
        if (mine.Count == 0) return new EligibilitySummary(0, 0, 0);

        int open = await Open(db, now).CountAsync(cancellationToken).ConfigureAwait(false);
        int eligible = 0, notEligible = 0, overLimit = 0, outside = 0, regionUnknown = 0, mainFieldMismatch = 0;
        foreach (var e in await CheckOpenAsync(db, now, mine, cancellationToken).ConfigureAwait(false))
        {
            var r = e.Result;
            if (r.CanBid)
            {
                eligible++;
                if (r.Region == RegionVerdict.Unknown) regionUnknown++;
                if (r.MainField == MainFieldVerdict.Mismatch) mainFieldMismatch++;
            }
            else if (r.Kind != EligibilityKind.Eligible) notEligible++;
            else if (r.Amount == AmountVerdict.Over) overLimit++;
            else outside++;
        }
        return new EligibilitySummary(eligible, notEligible, open - eligible - notEligible - overLimit - outside, overLimit, outside, regionUnknown, mainFieldMismatch);
    }

    public async Task<Eligibility?> CheckAsync(int noticeId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var mine = await LicensesAsync(db, cancellationToken).ConfigureAwait(false);
        if (mine.Count == 0) return null;
        long? estimated = await db.Notices.AsNoTracking().Where(n => n.Id == noticeId).Select(n => n.EstimatedPrice)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.NoticeLicenses.AsNoTracking().Where(l => l.NoticeId == noticeId)
            .Select(l => new { l.GroupNo, l.Name, l.Alternatives, l.MainFields })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var regions = await db.NoticeRegionLimits.AsNoTracking().Where(r => r.NoticeId == noticeId).Select(r => r.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var locations = await LocationsAsync(db, cancellationToken).ConfigureAwait(false);
        return Judge([.. rows.Select(r => Row(r.GroupNo, r.Name, r.Alternatives, r.MainFields))], mine, estimated, regions, locations);
    }

    /// <summary>공고 하나를 면허·금액·지역(빼는 기준)과 주력분야(참고)로 판정한다. 판정 순서는 여기 한 곳.</summary>
    private static Eligibility Judge(IReadOnlyList<LicenseRow> rows, Mine mine, long? estimated,
        IReadOnlyCollection<string> regions, IReadOnlyCollection<string> locations)
    {
        var e = LicenseEligibility.Check(rows, mine.Limits, estimated);
        var mainField = e.Kind == EligibilityKind.Eligible
            ? MainFieldEligibility.Check(rows.Where(r => r.GroupNo == e.GroupNo), mine.MainFields)
            : MainFieldVerdict.NoLimit;
        return e with { Region = RegionEligibility.Check(regions, locations), MainField = mainField };
    }

    private static LicenseRow Row(int groupNo, string name, string? alternatives, string? mainFields) =>
        new(groupNo, name, NoticeLicense.SplitAlternatives(alternatives), NoticeLicense.SplitMainFields(mainFields));

    /// <summary>내 면허 이름별 금액 상한(모르면 null)과 주력분야.</summary>
    private sealed record Mine(Dictionary<string, long?> Limits, Dictionary<string, IReadOnlyCollection<string>> MainFields)
    {
        public int Count => Limits.Count;
    }

    /// <summary>진행 중 공고 중 내 면허로 참여 가능하고 금액 상한도 넘지 않는 공고 번호. 목록 필터에서도 쓴다.</summary>
    internal static async Task<IReadOnlyList<int>> EligibleOpenNoticeIdsAsync(BidNoteDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var mine = await LicensesAsync(db, cancellationToken).ConfigureAwait(false);
        if (mine.Count == 0) return [];
        return [.. (await CheckOpenAsync(db, now, mine, cancellationToken).ConfigureAwait(false)).Where(x => x.Result.CanBid).Select(x => x.NoticeId)];
    }

    private sealed record OpenRow(int NoticeId, long? EstimatedPrice, LicenseRow Row);

    private static IQueryable<Notice> Open(BidNoteDbContext db, DateTime now) =>
        db.Notices.AsNoTracking().Where(x => x.IsActive && x.DueAt > now);

    /// <summary>면허 제한이 있는 진행 공고마다 면허·금액·지역을 판정한다 (면허 제한이 없는 공고는 빠진다).</summary>
    private static async Task<List<(int NoticeId, Eligibility Result)>> CheckOpenAsync(BidNoteDbContext db, DateTime now,
        Mine mine, CancellationToken cancellationToken)
    {
        var rows = await OpenLicenseRowsAsync(db, now, cancellationToken).ConfigureAwait(false);
        var regions = (await db.NoticeRegionLimits.AsNoTracking().Where(r => r.Notice!.IsActive && r.Notice.DueAt > now)
                .Select(r => new { r.NoticeId, r.Name }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(r => r.NoticeId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<string>)[.. g.Select(r => r.Name)]);
        var locations = await LocationsAsync(db, cancellationToken).ConfigureAwait(false);
        return [.. rows.GroupBy(r => r.NoticeId)
            .Select(g => (g.Key, Judge([.. g.Select(r => r.Row)], mine, g.First().EstimatedPrice, regions.GetValueOrDefault(g.Key, []), locations)))];
    }

    private static async Task<List<string>> LocationsAsync(BidNoteDbContext db, CancellationToken cancellationToken) =>
        await db.CompanyLocations.AsNoTracking().Select(x => x.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    private static async Task<List<OpenRow>> OpenLicenseRowsAsync(BidNoteDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var rows = await db.NoticeLicenses.AsNoTracking().Where(l => l.Notice!.IsActive && l.Notice.DueAt > now)
            .Select(l => new { l.NoticeId, l.Notice!.EstimatedPrice, l.GroupNo, l.Name, l.Alternatives, l.MainFields })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(r => new OpenRow(r.NoticeId, r.EstimatedPrice, Row(r.GroupNo, r.Name, r.Alternatives, r.MainFields)))];
    }

    private static async Task<Mine> LicensesAsync(BidNoteDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.CompanyLicenses.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Mine(
            rows.ToDictionary(x => x.Name, x => x.AmountLimit, StringComparer.Ordinal),
            rows.ToDictionary(x => x.Name, x => (IReadOnlyCollection<string>)x.MainFieldNames.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal));
    }

    private static string Cut(string? s, int max)
    {
        string text = (s ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }
}
