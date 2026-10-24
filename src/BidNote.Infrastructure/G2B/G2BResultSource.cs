using System.Globalization;
using System.Text.Json;
using BidNote.Application.Collecting;
using BidNote.Application.Results;
using BidNote.Domain.Notices;
using Microsoft.Extensions.Logging;

namespace BidNote.Infrastructure.G2B;

/// <summary>
/// 나라장터 낙찰정보서비스에서 공사 개찰 결과를 받는다 (2026-10-25 실제 호출로 확인).
/// 개찰 목록은 inqryDiv=1(개찰일시 기간), 업체별 순위는 inqryDiv=4, 예비가격은 inqryDiv=2에 공고번호를 붙여 조회한다.
/// </summary>
public sealed class G2BResultSource(G2BApiClient client, ILogger<G2BResultSource> logger) : IBidResultSource
{
    public const string OpeningApi = "getOpengResultListInfoCnstwk";
    public const string RankApi = "getOpengResultListInfoOpengCompt";
    public const string ReservePriceApi = "getOpengResultListInfoCnstwkPreparPcDetail";

    public string SourceSystem => SourceSystems.G2B;

    public async Task<IReadOnlyList<CollectedOpening>> CollectOpeningsAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var fetch = await client.FetchAwardAsync(OpeningApi, G2BApiClient.Window(from, to), cancellationToken).ConfigureAwait(false);
        if (!fetch.Complete)
            logger.LogWarning("개찰 목록을 일부만 받았습니다 ({Got}/{Total}건). 나머지는 다음 수집 때 받습니다.", fetch.Items.Count, fetch.TotalCount);
        return [.. fetch.Items.Select(x => MapOpening(x, fetch.RequestParameter))];
    }

    public async Task<CollectedRanking?> FetchRankingAsync(string bidNo, string bidSeq, string classNo, string rebidNo, CancellationToken cancellationToken)
    {
        string notice = $"bidNtceNo={Uri.EscapeDataString(bidNo)}&bidNtceOrd={Uri.EscapeDataString(bidSeq)}";
        var ranks = await client.FetchAwardAsync(RankApi, "inqryDiv=4&" + notice, cancellationToken).ConfigureAwait(false);
        var prices = await client.FetchAwardAsync(ReservePriceApi, "inqryDiv=2&" + notice, cancellationToken).ConfigureAwait(false);

        bool Same(JsonElement e) => (classNo.Length == 0 || Str(e, "bidClsfcNo") == classNo) && (rebidNo.Length == 0 || Str(e, "rbidNo") == rebidNo);
        var rankRows = ranks.Items.Where(Same).ToList();
        var priceRow = prices.Items.Where(Same).Cast<JsonElement?>().FirstOrDefault();
        if (rankRows.Count == 0 && priceRow is null) return null;

        return new CollectedRanking(
            priceRow is { } p ? NullIfZero(Long(p, "plnprc")) : null,
            priceRow is { } q ? NullIfZero(Long(q, "bssamt")) : null,
            [.. rankRows.Select(MapRank)],
            ranks.Complete);
    }

    /// <summary>1순위 정보는 "업체명^사업자번호^대표자^투찰금액^투찰률"로 한 칸에 온다.</summary>
    internal static CollectedOpening MapOpening(JsonElement row, string requestParameter)
    {
        string[] winner = Str(row, "opengCorpInfo").Split('^');
        string Part(int i) => i < winner.Length ? winner[i].Trim() : "";

        return new CollectedOpening
        {
            BidNo = Str(row, "bidNtceNo"),
            BidSeq = Str(row, "bidNtceOrd"),
            ClassNo = Str(row, "bidClsfcNo"),
            RebidNo = Str(row, "rbidNo"),
            Status = Str(row, "progrsDivCdNm"),
            ParticipantCount = int.TryParse(Str(row, "prtcptCnum"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0,
            OpenedAt = Date(Str(row, "opengDt")),
            WinnerName = Part(0),
            WinnerBizNo = Part(1),
            WinnerAmount = long.TryParse(Part(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out long amount) && amount > 0 ? amount : null,
            WinnerBidRate = Double(Part(4)),
            ApiName = OpeningApi,
            RequestParameter = requestParameter,
            RawJson = JsonSerializer.Serialize(row, CollectService.ReadableJson),
        };
    }

    internal static CollectedRank MapRank(JsonElement row) => new(
        int.TryParse(Str(row, "opengRank"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rank) ? rank : null,
        Str(row, "prcbdrNm"),
        Str(row, "prcbdrBizno"),
        NullIfZero(Long(row, "bidprcAmt")),
        Double(Str(row, "bidprcrt")),
        Str(row, "rmrk"));

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()).Trim() : "";

    private static long Long(JsonElement e, string name) =>
        long.TryParse(Str(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0;

    private static long? NullIfZero(long v) => v > 0 ? v : null;

    private static double? Double(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;

    private static DateTime? Date(string s) =>
        DateTime.TryParseExact(s, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}

/// <summary>가상 모드용. 개찰 결과를 만들지 않는다.</summary>
public sealed class FakeResultSource : IBidResultSource
{
    public string SourceSystem => SourceSystems.Fake;

    public Task<IReadOnlyList<CollectedOpening>> CollectOpeningsAsync(DateTime from, DateTime to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CollectedOpening>>([]);

    public Task<CollectedRanking?> FetchRankingAsync(string bidNo, string bidSeq, string classNo, string rebidNo, CancellationToken cancellationToken) =>
        Task.FromResult<CollectedRanking?>(null);
}
