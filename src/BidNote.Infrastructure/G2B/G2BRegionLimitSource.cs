using System.Globalization;
using System.Text.Json;
using BidNote.Application.Collecting;
using BidNote.Domain.Notices;
using Microsoft.Extensions.Logging;

namespace BidNote.Infrastructure.G2B;

/// <summary>
/// 나라장터 참가 가능 지역 (2026-10-25 실제 호출로 확인). 공사·용역·물품이 섞여 오므로 공사(bsnsDivNm)만 쓴다.
/// 한 공고가 지역마다 한 줄씩(lmtSno) 오므로 공고번호+차수로 묶는다.
/// </summary>
public sealed class G2BRegionLimitSource(G2BApiClient client, ILogger<G2BRegionLimitSource> logger) : IRegionLimitSource
{
    public const string RegionApi = "getBidPblancListInfoPrtcptPsblRgn";
    private const string Construction = "공사";

    public string SourceSystem => SourceSystems.G2B;

    /// <summary>한 번에 조회하는 최대 기간. 길게 요청하면 "입력범위값 초과"(resultCode 07)로 거절된다 (2026-10-26, 60일 요청에서 확인).</summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<CollectedRegionLimit>> CollectAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var all = new List<CollectedRegionLimit>();
        foreach (var (start, end) in Windows(from, to))
        {
            var fetch = await client.FetchAllAsync(RegionApi, start, end, cancellationToken).ConfigureAwait(false);
            if (!fetch.Complete)
                logger.LogWarning("참가 가능 지역을 일부만 받았습니다 ({Got}/{Total}건). 나머지는 다음 수집 때 받습니다.", fetch.Items.Count, fetch.TotalCount);
            all.AddRange(Group(fetch.Items, fetch.RequestParameter));
        }
        return all;
    }

    /// <summary>기간을 <see cref="MaxWindow"/> 이하로 나눈다.</summary>
    internal static IEnumerable<(DateTime From, DateTime To)> Windows(DateTime from, DateTime to)
    {
        for (DateTime start = from; start < to; start += MaxWindow)
            yield return (start, start + MaxWindow < to ? start + MaxWindow : to);
    }

    internal static IReadOnlyList<CollectedRegionLimit> Group(IEnumerable<JsonElement> rows, string requestParameter) =>
        [.. rows.Where(r => Str(r, "bsnsDivNm") == Construction)
            .GroupBy(r => (BidNo: Str(r, "bidNtceNo"), BidSeq: Str(r, "bidNtceOrd")))
            .Where(g => g.Key.BidNo.Length > 0)
            .Select(g =>
            {
                var ordered = g.OrderBy(r => int.TryParse(Str(r, "lmtSno"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0).ToList();
                return new CollectedRegionLimit(g.Key.BidNo, g.Key.BidSeq,
                    [.. ordered.Select(r => Str(r, "prtcptPsblRgnNm")).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal)],
                    JsonSerializer.Serialize(ordered, CollectService.ReadableJson), RegionApi, requestParameter);
            })];

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()).Trim() : "";
}

/// <summary>가상 모드용. 참가 가능 지역을 만들지 않는다.</summary>
public sealed class FakeRegionLimitSource : IRegionLimitSource
{
    public string SourceSystem => SourceSystems.Fake;

    public Task<IReadOnlyList<CollectedRegionLimit>> CollectAsync(DateTime from, DateTime to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CollectedRegionLimit>>([]);
}
