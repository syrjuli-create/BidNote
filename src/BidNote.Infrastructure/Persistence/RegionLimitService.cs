using System.Security.Cryptography;
using System.Text;
using BidNote.Application.Collecting;
using BidNote.Domain.Collecting;
using BidNote.Domain.Notices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BidNote.Infrastructure.Persistence;

/// <summary>
/// 참가 가능 지역 저장. 공고 원문과 따로 받아 공고 원문 Hash(정정 이력)에 영향을 주지 않는다.
/// 이미 저장된 공고에만 붙이고, 같은 공고가 차수만 달리 오면 가장 높은 차수를 쓴다. 원문은 바뀐 경우에만 남긴다.
/// </summary>
public sealed class RegionLimitService(
    IDbContextFactory<BidNoteDbContext> factory,
    IRegionLimitSource source,
    TimeProvider timeProvider,
    ILogger<RegionLimitService> logger) : IRegionLimitService
{
    public async Task<bool> HasAnyAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Notices.AnyAsync(n => n.RegionLimitHash != "", cancellationToken).ConfigureAwait(false);
    }

    public async Task<DateTime?> OldestOpenPostedAtAsync(DateTime now, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Notices.AsNoTracking().Where(n => n.IsActive && n.DueAt > now && n.BusinessType == BusinessType.Construction)
            .OrderBy(n => n.PostedAt).Select(n => (DateTime?)n.PostedAt).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CollectAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var rows = await source.CollectAsync(from, to, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0) return 0;
        var latest = rows.GroupBy(x => x.BidNo, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(x => x.BidSeq, StringComparer.Ordinal).First())
            .ToList();

        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var bidNos = latest.Select(x => x.BidNo).ToList();
        var notices = await db.Notices.Include(n => n.RegionLimits)
            .Where(n => n.SourceSystem == source.SourceSystem && n.BusinessType == BusinessType.Construction && bidNos.Contains(n.BidNo))
            .ToDictionaryAsync(n => n.BidNo, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);

        DateTime now = timeProvider.GetLocalNow().DateTime;
        int updated = 0, missing = 0;
        foreach (var row in latest)
        {
            if (!notices.TryGetValue(row.BidNo, out var notice))
            {
                missing++;
                continue;
            }
            // 공고보다 낮은 차수의 지역은 무시한다 (정정 전 내용으로 되돌리지 않는다).
            if (string.CompareOrdinal(row.BidSeq, notice.BidSeq) < 0) continue;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.RawJson)));
            if (notice.RegionLimitHash == hash) continue;

            notice.RegionLimits.Clear();
            notice.RegionLimits.AddRange(row.Regions.Select(r => new NoticeRegionLimit
            {
                Name = r.Length <= NoticeRegionLimit.MaxNameLength ? r : r[..NoticeRegionLimit.MaxNameLength],
            }));
            notice.RegionLimitHash = hash;
            db.ApiRawResponses.Add(new ApiRawResponse
            {
                SourceSystem = source.SourceSystem,
                ApiName = row.ApiName,
                RequestParameter = row.RequestParameter,
                BusinessType = BusinessType.Construction,
                BidNo = row.BidNo,
                ResponseJson = row.RawJson,
                HttpStatus = 200,
                CollectedAt = now,
                ParsedAt = now,
                ParseVersion = CollectService.ParseVersion,
            });
            updated++;
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("{Source} 참가 가능 지역: 받은 공고 {Count}건, 반영 {Updated}건, 공고를 받지 않아 건너뜀 {Missing}건", source.SourceSystem, latest.Count, updated, missing);
        return updated;
    }
}
