using System.Text.Json;
using BidNote.Application.Collecting;
using BidNote.Domain.Notices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BidNote.Infrastructure.G2B;

/// <summary>
/// API 키를 받기 전 화면 개발용 가상 수집기. 실제 나라장터 응답 형식과 다를 수 있으며,
/// 날짜는 프로그램을 켠 날을 기준으로 만든다.
/// </summary>
public sealed class FakeG2BCollector : IBidCollector
{
    public const string ApiName = "FAKE.getBidPblancListInfoCnstwk";

    private readonly ILogger<FakeG2BCollector> logger;
    private readonly IApiUsageTracker usage;
    private readonly DateTime anchor;
    private readonly string keyHint;

    public FakeG2BCollector(IOptions<G2BOptions> options, TimeProvider timeProvider, IApiUsageTracker usage, ILogger<FakeG2BCollector> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (string.IsNullOrWhiteSpace(options.Value.ServiceKey))
            throw new InvalidOperationException("G2B:ServiceKey가 비어 있습니다. 가상 모드에서도 아무 값이나 넣어 주세요.");
        this.logger = logger;
        this.usage = usage;
        anchor = timeProvider.GetLocalNow().Date;
        keyHint = options.Value.ServiceKey.Length <= 4 ? "****" : "****" + options.Value.ServiceKey[^4..];
    }

    public string SourceSystem => SourceSystems.Fake;

    public async Task<IReadOnlyList<CollectedNotice>> CollectAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        // 실제 수집기와 같은 흐름: 한도 확인 → 호출 → 호출 기록. 가상 모드에서도 사용량 화면을 확인할 수 있게 기록한다.
        if (!await usage.CanCallAsync(ApiName, cancellationToken).ConfigureAwait(false)) return [];
        logger.LogInformation("가상 수집기 사용 중 (키 {KeyHint}). 실제 나라장터를 호출하지 않습니다.", keyHint);
        await usage.RecordAsync(ApiName, cancellationToken).ConfigureAwait(false);
        return [.. Samples().Select(s => ToNotice(s, from, to))];
    }

    /// <summary>가상 공고는 처음부터 기초금액이 들어 있어 따로 받을 것이 없다.</summary>
    public Task<IReadOnlyList<CollectedBasePrice>> CollectBasePricesAsync(DateTime from, DateTime to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CollectedBasePrice>>([]);

    // 공고번호, 차수, 공고명, 발주기관, 수요기관, 지역, 면허, 기초금액, 하한율, A값, 순공사원가, 공고일(앵커 기준 일), 마감(일, 시)
    private sealed record Sample(string No, string Seq, string Name, string Agency, string Demand, string Region, string License,
        long Base, double Low, long A, long Net, int PostedDays, int DueDays, int DueHour);

    private static IEnumerable<Sample> Samples() =>
    [
        new("FAKE-0001", "01", "○○초등학교 화장실 개선공사", "대전광역시 ○○교육지원청", "○○초등학교", "대전", "실내건축", 186_400_000, 87.745, 7_420_000, 152_300_000, -8, 4, 10),
        new("FAKE-0002", "00", "○○천 하천 정비공사", "충청남도 ○○군", "○○군 건설과", "충남", "토목", 1_248_000_000, 87.745, 48_900_000, 1_012_000_000, -6, 7, 10),
        new("FAKE-0003", "00", "○○동 행정복지센터 리모델링공사", "세종특별자치시", "○○동 행정복지센터", "세종", "건축", 532_700_000, 87.745, 21_300_000, 438_000_000, -7, 5, 10),
        new("FAKE-0004", "00", "○○로 도로 포장 보수공사", "대전광역시 ○○구", "○○구 도로과", "대전", "포장", 94_300_000, 87.745, 3_110_000, 78_600_000, -10, 1, 10),
        new("FAKE-0005", "00", "○○공원 조경시설물 정비공사", "충청북도 ○○시", "○○시 공원녹지과", "충북", "조경시설물", 72_800_000, 87.745, 2_480_000, 60_100_000, -4, 6, 10),
        new("FAKE-0006", "02", "○○중학교 소방시설 개선공사", "충청남도 ○○교육지원청", "○○중학교", "충남", "소방", 143_500_000, 87.745, 5_260_000, 118_900_000, -5, 8, 10),
        new("FAKE-0007", "00", "○○청사 전기설비 교체공사", "대전광역시 ○○청", "○○청 시설관리과", "대전", "전기", 268_900_000, 87.745, 9_870_000, 221_400_000, -11, 1, 14),
        new("FAKE-0008", "00", "○○지구 하수관로 정비공사", "충청남도 ○○시", "○○시 하수도과", "충남", "상하수도", 2_315_000_000, 86.745, 92_400_000, 1_898_000_000, -3, 12, 10),
        new("FAKE-0009", "00", "○○마을 경로당 신축공사", "세종특별자치시", "○○면사무소", "세종", "건축", 418_000_000, 87.745, 16_800_000, 343_200_000, -4, 11, 10),
        new("FAKE-0010", "00", "○○초 다목적강당 지붕 방수공사", "대전광역시 ○○교육지원청", "○○초등학교", "대전", "습식방수", 61_200_000, 87.745, 2_040_000, 50_300_000, -9, 3, 10),
        new("FAKE-0011", "00", "○○체육관 냉난방설비 교체공사", "충청북도 ○○군", "○○군 체육시설사업소", "충북", "기계설비", 356_000_000, 87.745, 13_100_000, 292_700_000, -2, 10, 10),
        new("FAKE-0012", "01", "○○ 공공청사 통신망 구축공사", "대전광역시", "○○사업소", "대전", "정보통신", 127_300_000, 87.745, 4_520_000, 104_600_000, -7, 5, 14),
    ];

    private CollectedNotice ToNotice(Sample s, DateTime from, DateTime to)
    {
        DateTime posted = anchor.AddDays(s.PostedDays).AddHours(9);
        DateTime due = anchor.AddDays(s.DueDays).AddHours(s.DueHour);
        DateTime modified = s.Seq == "00" ? posted : anchor.AddDays(-1).AddHours(15);
        string raw = JsonSerializer.Serialize(new
        {
            fake = true,
            bidNtceNo = s.No,
            bidNtceOrd = s.Seq,
            bidNtceNm = s.Name,
            ntceInsttNm = s.Agency,
            dminsttNm = s.Demand,
            prtcptPsblRgnNm = s.Region,
            indstrytyNm = s.License,
            bssamt = s.Base,
            sucsfbidLwltRate = s.Low,
            aValue = s.A,
            netCost = s.Net,
            bidNtceDt = posted,
            bidClseDt = due,
            opengDt = due.AddHours(1),
            chgDt = modified,
        });

        return new CollectedNotice
        {
            SourceSystem = SourceSystem,
            BusinessType = BusinessType.Construction,
            BidNo = s.No,
            BidSeq = s.Seq,
            Name = s.Name,
            Agency = s.Agency,
            DemandAgency = s.Demand,
            Region = s.Region,
            SiteRegionName = s.Region,
            License = s.License,
            Licenses = [new CollectedLicense(1, s.License)],
            BasePrice = s.Base,
            LowerLimitRate = s.Low,
            AValue = s.A,
            NetCost = s.Net,
            PostedAt = posted,
            DueAt = due,
            OpenAt = due.AddHours(1),
            LastModifiedAt = modified,
            ApiName = ApiName,
            RequestParameter = $"inqryBgnDt={from:yyyyMMddHHmm}&inqryEndDt={to:yyyyMMddHHmm}",
            RawJson = raw,
        };
    }
}
