using System.Globalization;
using System.Text.Json;
using BidNote.Application.Collecting;
using BidNote.Application.Notices;
using BidNote.Infrastructure.G2B;
using Microsoft.EntityFrameworkCore;

namespace BidNote.Infrastructure.Persistence;

public sealed class NoticeDetailQueries(BidNoteDbContext db) : INoticeDetailQueries
{
    // 나라장터 공고 원문에서 상세 화면에 더 보여 줄 항목
    private static readonly (string Field, string Label, bool Money)[] ExtraFields =
    [
        ("ntceKindNm", "공고 종류", false),
        ("cntrctCnclsMthdNm", "계약방법", false),
        ("sucsfbidMthdNm", "낙찰방법", false),
        ("bidMethdNm", "입찰방식", false),
        ("bdgtAmt", "예산금액", true),
        ("presmptPrce", "추정가격", true),
        ("rgnLmtBidLocplcJdgmBssNm", "지역제한 판단 기준", false),
        ("cmmnSpldmdMethdNm", "공동수급 방식", false),
        ("opengPlce", "개찰 장소", false),
    ];

    public async Task<NoticeDetail?> GetAsync(int id, CancellationToken cancellationToken)
    {
        var n = await db.Notices.AsNoTracking().Include(x => x.Licenses).Include(x => x.Histories).AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        if (n is null) return null;

        string? raw = await db.ApiRawResponses.AsNoTracking()
            .Where(r => r.SourceSystem == n.SourceSystem && r.BusinessType == n.BusinessType && r.BidNo == n.BidNo && r.ApiName != G2BCollector.BasePriceApi)
            .OrderByDescending(r => r.Id).Select(r => r.ResponseJson).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        string? priceRaw = await db.ApiRawResponses.AsNoTracking()
            .Where(r => r.SourceSystem == n.SourceSystem && r.BusinessType == n.BusinessType && r.BidNo == n.BidNo && r.ApiName == G2BCollector.BasePriceApi)
            .OrderByDescending(r => r.Id).Select(r => r.ResponseJson).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var histories = n.Histories.OrderBy(h => h.DetectedAt).ThenBy(h => h.Id)
            .Select(h => (h.DetectedAt, h.ChangeType, h.SnapshotJson)).ToList();

        return new NoticeDetail
        {
            Id = n.Id,
            SourceSystem = n.SourceSystem,
            BidNo = n.BidNo,
            BidSeq = n.BidSeq,
            Name = n.Name,
            Agency = n.Agency,
            DemandAgency = n.DemandAgency,
            Region = n.Region,
            SiteRegionName = n.SiteRegionName,
            License = n.License,
            BasePrice = n.BasePrice,
            BasePriceOpenedAt = n.BasePriceOpenedAt,
            ReservePriceRange = n.ReservePriceRange,
            AValue = n.AValue,
            NetCost = n.NetCost,
            LowerLimitRate = n.LowerLimitRate,
            PostedAt = n.PostedAt,
            DueAt = n.DueAt,
            OpenAt = n.OpenAt,
            IsActive = n.IsActive,
            IsRebid = n.IsRebid,
            CorrectionCount = n.CorrectionCount,
            DetailUrl = n.DetailUrl,
            LastCheckedAt = n.LastCheckedAt,
            Licenses = [.. n.Licenses.GroupBy(l => l.GroupNo).OrderBy(g => g.Key)
                .Select(g => new LicenseGroup(g.Key, [.. g.OrderBy(l => l.Id).Select(l => new LicenseRequirement(l.Name, l.AlternativeNames))]))],
            Changes = NoticeHistoryDiff.Build(histories, CollectService.SnapshotJson(n)),
            Extras = raw is null ? [] : Extras(raw),
            CostItems = priceRaw is null ? [] : CostItemsOf(priceRaw),
        };
    }

    private static IReadOnlyList<CostItem> CostItemsOf(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return G2BCollector.CostItems(doc.RootElement);
    }

    internal static IReadOnlyList<LabeledValue> Extras(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement.TryGetProperty("notice", out var notice) ? notice : doc.RootElement;
        var list = new List<LabeledValue>();
        foreach (var (field, label, money) in ExtraFields)
        {
            if (!root.TryGetProperty(field, out var v)) continue;
            string text = v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : v.ToString();
            if (text.Length == 0) continue;
            if (money && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long amount))
            {
                if (amount <= 0) continue;
                text = amount.ToString("N0", CultureInfo.GetCultureInfo("ko-KR")) + "원";
            }
            list.Add(new LabeledValue(label, text));
        }
        return list;
    }
}
