using BidNote.Application.Company;
using BidNote.Domain.Company;
using BidNote.Domain.Notices;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Infrastructure.Persistence;

/// <summary>회사·면허 정보 저장과 면허로 참여 가능한 공고 판정. 화면 버튼에서 불리므로 호출마다 DB 연결을 새로 연다.</summary>
public sealed class CompanyStore(IDbContextFactory<BidNoteDbContext> factory, TimeProvider timeProvider) : ICompanyStore
{
    public async Task<CompanyInfo> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var p = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == CompanyProfile.SingletonId, cancellationToken).ConfigureAwait(false);
        var licenses = await LicensesAsync(db, cancellationToken).ConfigureAwait(false);
        return new CompanyInfo(p?.Name ?? "", p?.BizNo ?? "", p?.Region ?? "", p?.Email ?? "", [.. licenses.Order(StringComparer.CurrentCulture)]);
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

    public async Task<IReadOnlyList<LicenseOption>> LicenseOptionsAsync(DateTime now, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await OpenLicenseRowsAsync(db, now, cancellationToken).ConfigureAwait(false);
        return [.. rows
            .SelectMany(r => r.Alternatives.Prepend(r.Name).Select(name => (r.NoticeId, Name: name)))
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
        var rows = await OpenLicenseRowsAsync(db, now, cancellationToken).ConfigureAwait(false);
        int eligible = 0, notEligible = 0;
        foreach (var notice in rows.GroupBy(r => r.NoticeId))
        {
            if (LicenseEligibility.Check(notice.Select(r => r.Row), mine).Kind == EligibilityKind.Eligible) eligible++;
            else notEligible++;
        }
        return new EligibilitySummary(eligible, notEligible, open - eligible - notEligible);
    }

    public async Task<Eligibility?> CheckAsync(int noticeId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var mine = await LicensesAsync(db, cancellationToken).ConfigureAwait(false);
        if (mine.Count == 0) return null;
        var rows = await db.NoticeLicenses.AsNoTracking().Where(l => l.NoticeId == noticeId)
            .Select(l => new { l.GroupNo, l.Name, l.Alternatives })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return LicenseEligibility.Check(rows.Select(r => new LicenseRow(r.GroupNo, r.Name, NoticeLicense.SplitAlternatives(r.Alternatives))), mine);
    }

    /// <summary>진행 중 공고 중 내 면허로 참여 가능한 공고 번호. 목록 필터에서도 쓴다.</summary>
    internal static async Task<IReadOnlyList<int>> EligibleOpenNoticeIdsAsync(BidNoteDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var mine = await LicensesAsync(db, cancellationToken).ConfigureAwait(false);
        if (mine.Count == 0) return [];
        var rows = await OpenLicenseRowsAsync(db, now, cancellationToken).ConfigureAwait(false);
        return [.. rows.GroupBy(r => r.NoticeId)
            .Where(g => LicenseEligibility.Check(g.Select(r => r.Row), mine).Kind == EligibilityKind.Eligible)
            .Select(g => g.Key)];
    }

    private sealed record OpenRow(int NoticeId, LicenseRow Row)
    {
        public string Name => Row.Name;
        public IReadOnlyList<string> Alternatives => Row.Alternatives;
    }

    private static IQueryable<Notice> Open(BidNoteDbContext db, DateTime now) =>
        db.Notices.AsNoTracking().Where(x => x.IsActive && x.DueAt > now);

    private static async Task<List<OpenRow>> OpenLicenseRowsAsync(BidNoteDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var open = Open(db, now);
        var rows = await db.NoticeLicenses.AsNoTracking().Where(l => open.Any(n => n.Id == l.NoticeId))
            .Select(l => new { l.NoticeId, l.GroupNo, l.Name, l.Alternatives })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(r => new OpenRow(r.NoticeId, new LicenseRow(r.GroupNo, r.Name, NoticeLicense.SplitAlternatives(r.Alternatives))))];
    }

    private static async Task<HashSet<string>> LicensesAsync(BidNoteDbContext db, CancellationToken cancellationToken) =>
        (await db.CompanyLicenses.AsNoTracking().Select(x => x.Name).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);

    private static string Cut(string? s, int max)
    {
        string text = (s ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }
}
