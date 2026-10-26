using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using BidNote.Domain.Collecting;
using BidNote.Domain.Notices;
using Microsoft.Extensions.Logging;

namespace BidNote.Application.Collecting;

public sealed record CollectResult(int Added, int Changed, int Unchanged)
{
    /// <summary>기초금액을 새로 채우거나 바꾼 공고 수.</summary>
    public int BasePricesUpdated { get; init; }

    /// <summary>이번 수집에서 저장한 원문 수. 새 공고·바뀐 공고·바뀐 기초금액만 저장한다.</summary>
    public int RawSaved => Added + Changed + BasePricesUpdated;
}

/// <summary>
/// 수집기에서 받은 공고를 저장한다. 원문은 새 공고이거나 바뀐 공고일 때만 남기고,
/// 이미 있는 공고의 원문이 바뀌었으면 바뀌기 전 상태를 이력으로 남긴 뒤 갱신한다.
/// </summary>
public sealed class CollectService(
    IEnumerable<IBidCollector> collectors,
    INoticeRepository repository,
    TimeProvider timeProvider,
    ILogger<CollectService> logger)
{
    /// <summary>원문을 해석하는 방식이 바뀌면 올린다. 원문 재처리 대상을 고를 때 쓴다.</summary>
    public const int ParseVersion = 1;

    /// <summary>한글을 유니코드 이스케이프로 바꾸지 않고 그대로 저장한다 (DB에서 읽기 쉽고 용량도 절반).</summary>
    public static readonly JsonSerializerOptions ReadableJson = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    public async Task<CollectResult> CollectAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        int added = 0, changed = 0, unchanged = 0, basePrices = 0;
        foreach (var collector in collectors)
        {
            var items = await collector.CollectAsync(from, to, cancellationToken).ConfigureAwait(false);
            // 같은 공고가 차수만 달리 여러 번 올 수 있다 (최초 000 → 정정 001). 차수 순서대로 처리해 정정 이력을 남기고,
            // 아직 DB에 저장하기 전인 공고도 다시 찾을 수 있게 이번 수집에서 다룬 공고를 기억해 둔다.
            var seen = new Dictionary<(string, BusinessType, string), Notice>();
            foreach (var item in items.OrderBy(x => x.BidNo, StringComparer.Ordinal).ThenBy(x => x.BidSeq, StringComparer.Ordinal))
            {
                switch (await SaveAsync(item, seen, cancellationToken).ConfigureAwait(false))
                {
                    case SaveOutcome.Added: added++; break;
                    case SaveOutcome.Changed: changed++; break;
                    default: unchanged++; break;
                }
            }
            await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("{Source} 수집 완료: {From:yyyy-MM-dd HH:mm}~{To:yyyy-MM-dd HH:mm}, {Count}건", collector.SourceSystem, from, to, items.Count);

            basePrices += await ApplyBasePricesAsync(collector, from, to, seen, cancellationToken).ConfigureAwait(false);
            await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return new CollectResult(added, changed, unchanged) { BasePricesUpdated = basePrices };
    }

    /// <summary>
    /// 기초금액을 이미 저장된 공고에 채운다. 아직 받지 못한 공고의 기초금액은 건너뛰고,
    /// 같은 내용이 다시 오면 저장하지 않는다. 기초금액 공개는 정정이 아니므로 변경 이력은 남기지 않는다.
    /// </summary>
    private async Task<int> ApplyBasePricesAsync(IBidCollector collector, DateTime from, DateTime to,
        Dictionary<(string, BusinessType, string), Notice> seen, CancellationToken cancellationToken)
    {
        var rows = await collector.CollectBasePricesAsync(from, to, cancellationToken).ConfigureAwait(false);
        int updated = 0, missing = 0;
        DateTime now = timeProvider.GetLocalNow().DateTime;
        foreach (var row in rows.OrderBy(x => x.BidNo, StringComparer.Ordinal).ThenBy(x => x.BidSeq, StringComparer.Ordinal))
        {
            var key = (row.SourceSystem, row.BusinessType, row.BidNo);
            var notice = seen.TryGetValue(key, out var cached) ? cached
                : await repository.FindAsync(row.SourceSystem, row.BusinessType, row.BidNo, cancellationToken).ConfigureAwait(false);
            if (notice is null)
            {
                missing++;
                continue;
            }
            seen[key] = notice;

            string hash = HashOf(row.RawJson);
            if (notice.BasePriceHash == hash) continue;

            notice.BasePrice = row.BasePrice;
            notice.AValue = row.AValue;
            notice.NetCost = row.NetCost;
            notice.BasePriceOpenedAt = row.OpenedAt;
            notice.ReservePriceRange = row.ReservePriceRange;
            notice.BasePriceHash = hash;
            repository.AddRaw(new ApiRawResponse
            {
                SourceSystem = row.SourceSystem,
                ApiName = row.ApiName,
                RequestParameter = row.RequestParameter,
                BusinessType = row.BusinessType,
                BidNo = row.BidNo,
                ResponseJson = row.RawJson,
                HttpStatus = 200,
                CollectedAt = now,
                ParsedAt = now,
                ParseVersion = ParseVersion,
            });
            updated++;
        }
        if (rows.Count > 0)
            logger.LogInformation("{Source} 기초금액: 받은 {Count}건, 반영 {Updated}건, 공고를 아직 받지 못해 건너뜀 {Missing}건", collector.SourceSystem, rows.Count, updated, missing);
        return updated;
    }

    private enum SaveOutcome { Added, Changed, Unchanged }

    private async Task<SaveOutcome> SaveAsync(CollectedNotice item, Dictionary<(string, BusinessType, string), Notice> seen, CancellationToken cancellationToken)
    {
        DateTime now = timeProvider.GetLocalNow().DateTime;
        string hash = HashOf(item.RawJson);
        var key = (item.SourceSystem, item.BusinessType, item.BidNo);

        var notice = seen.TryGetValue(key, out var cached) ? cached
            : await repository.FindAsync(item.SourceSystem, item.BusinessType, item.BidNo, cancellationToken).ConfigureAwait(false);
        if (notice is null)
        {
            notice = new Notice { SourceSystem = item.SourceSystem, BusinessType = item.BusinessType, BidNo = item.BidNo, FirstCollectedAt = now };
            Apply(notice, item, hash, now);
            repository.Add(notice);
            SaveRaw(item, now);
            seen[key] = notice;
            return SaveOutcome.Added;
        }
        seen[key] = notice;

        // 이미 더 높은 차수를 가진 공고에 예전 차수가 다시 오면 무시한다 (정정 전 내용으로 되돌리지 않는다).
        if (string.CompareOrdinal(item.BidSeq, notice.BidSeq) < 0)
        {
            notice.LastCheckedAt = now;
            return SaveOutcome.Unchanged;
        }

        // 원문이 같으면 확인 시각만 바꾼다. 원문은 새 공고이거나 바뀐 경우에만 저장해 용량이 늘지 않게 한다.
        if (notice.RawHash == hash)
        {
            notice.LastCheckedAt = now;
            return SaveOutcome.Unchanged;
        }

        SaveRaw(item, now);

        notice.Histories.Add(new NoticeHistory
        {
            BidSeq = notice.BidSeq,
            ChangeType = ChangeTypeOf(notice, item),
            SnapshotJson = Snapshot(notice),
            RawHash = notice.RawHash,
            DetectedAt = now,
        });
        bool seqChanged = item.BidSeq != notice.BidSeq;
        Apply(notice, item, hash, now);
        if (seqChanged && !int.TryParse(item.BidSeq, out _)) notice.CorrectionCount++;
        return SaveOutcome.Changed;
    }

    private void SaveRaw(CollectedNotice item, DateTime now) => repository.AddRaw(new ApiRawResponse
    {
        SourceSystem = item.SourceSystem,
        ApiName = item.ApiName,
        RequestParameter = item.RequestParameter,
        BusinessType = item.BusinessType,
        BidNo = item.BidNo,
        ResponseJson = item.RawJson,
        HttpStatus = item.HttpStatus,
        CollectedAt = now,
        ParsedAt = now,
        ParseVersion = ParseVersion,
    });

    private static NoticeChangeType ChangeTypeOf(Notice current, CollectedNotice incoming) =>
        incoming.IsCancelled ? NoticeChangeType.Cancelled
        : incoming.IsRebid && !current.IsRebid ? NoticeChangeType.Rebid
        : incoming.BidSeq != current.BidSeq ? NoticeChangeType.Corrected
        : NoticeChangeType.Updated;

    private static void Apply(Notice n, CollectedNotice i, string hash, DateTime now)
    {
        n.BidSeq = i.BidSeq;
        // 나라장터 공고차수는 정정할 때마다 1씩 오른다 ("00" 최초, "01" 정정 1차). 실제 응답으로 확인 필요.
        if (int.TryParse(i.BidSeq, out int seq)) n.CorrectionCount = seq;
        n.Name = i.Name;
        n.Agency = i.Agency;
        n.DemandAgency = i.DemandAgency;
        n.Region = i.Region;
        n.SiteRegionName = i.SiteRegionName;
        n.ContractMethod = i.ContractMethod;
        n.License = i.License.Length > 0 ? i.License : i.Licenses.FirstOrDefault()?.Name ?? "";
        n.DetailUrl = i.DetailUrl;
        // 면허 제한은 통째로 바꾼다 (원문이 바뀐 경우에만 여기까지 온다).
        n.Licenses.Clear();
        n.Licenses.AddRange(i.Licenses.Select(l => new NoticeLicense
        {
            GroupNo = l.GroupNo,
            Name = l.Name,
            Alternatives = NoticeLicense.JoinAlternatives(l.Alternatives ?? []),
            MainFields = NoticeLicense.JoinMainFields(l.MainFields ?? []),
        }));
        // 나라장터 공고 목록에는 기초금액이 없다. 따로 받아 채운 기초금액을 정정 공고가 빈 값으로 지우지 않게 한다.
        if (i.BasePrice is not null)
        {
            n.BasePrice = i.BasePrice;
            n.AValue = i.AValue;
            n.NetCost = i.NetCost;
        }
        n.EstimatedPrice = i.EstimatedPrice;
        n.PerformanceLimited = i.PerformanceLimited;
        n.PerformanceDocDueAt = i.PerformanceDocDueAt;
        n.PerformanceDocMethod = i.PerformanceDocMethod;
        n.LowerLimitRate = i.LowerLimitRate;
        n.PostedAt = i.PostedAt;
        n.DueAt = i.DueAt;
        n.OpenAt = i.OpenAt;
        n.IsRebid = i.IsRebid;
        n.IsActive = !i.IsCancelled;
        n.LastModifiedAt = i.LastModifiedAt;
        n.LastCheckedAt = now;
        n.RawHash = hash;
    }

    private static string Snapshot(Notice n) => SnapshotJson(n);

    /// <summary>공고 상태를 이력용 JSON으로 만든다. 상세 화면에서 이력끼리 비교할 때도 같은 모양을 쓴다.</summary>
    public static string SnapshotJson(Notice n) => JsonSerializer.Serialize(new
    {
        n.BidSeq,
        n.Name,
        n.Agency,
        n.DemandAgency,
        n.Region,
        n.SiteRegionName,
        n.License,
        Licenses = n.Licenses.Select(l => new { l.GroupNo, l.Name }),
        n.BasePrice,
        n.LowerLimitRate,
        n.AValue,
        n.NetCost,
        n.PostedAt,
        n.DueAt,
        n.OpenAt,
        n.IsRebid,
        n.IsActive,
        n.LastModifiedAt,
    }, ReadableJson);

    private static string HashOf(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
