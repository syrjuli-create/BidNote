using System.Globalization;
using System.Text.Json;
using BidNote.Application.Collecting;
using BidNote.Application.Notices;
using BidNote.Domain.Notices;
using Microsoft.Extensions.Logging;

namespace BidNote.Infrastructure.G2B;

/// <summary>나라장터 공사 공고 수집기. 공고 목록과 면허 제한을 공고번호+차수로 합친다.</summary>
public sealed class G2BCollector(G2BApiClient client, ILogger<G2BCollector> logger) : IBidCollector
{
    public const string ListApi = "getBidPblancListInfoCnstwk";
    public const string LicenseApi = "getBidPblancListInfoLicenseLimit";
    public const string BasePriceApi = "getBidPblancListInfoCnstwkBsisAmount";

    public string SourceSystem => SourceSystems.G2B;

    public async Task<IReadOnlyList<CollectedNotice>> CollectAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var list = await client.FetchAllAsync(ListApi, from, to, cancellationToken).ConfigureAwait(false);
        if (list.Items.Count == 0) return [];

        var licenses = await client.FetchAllAsync(LicenseApi, from, to, cancellationToken).ConfigureAwait(false);
        if (!licenses.Complete)
            logger.LogWarning("면허 제한을 일부만 받았습니다 ({Got}/{Total}건). 면허가 빠진 공고는 다음 수집 때 채워집니다.", licenses.Items.Count, licenses.TotalCount);

        var byNotice = licenses.Items
            .GroupBy(x => (Str(x, "bidNtceNo"), Str(x, "bidNtceOrd")))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => Int(x, "lmtGrpNo")).ThenBy(x => Int(x, "lmtSno")).ToList());

        var result = new List<CollectedNotice>(list.Items.Count);
        foreach (var item in list.Items)
        {
            var key = (Str(item, "bidNtceNo"), Str(item, "bidNtceOrd"));
            var rows = byNotice.TryGetValue(key, out var found) ? found : [];
            result.Add(Map(item, rows, list.RequestParameter));
        }
        return result;
    }

    public async Task<IReadOnlyList<CollectedBasePrice>> CollectBasePricesAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var fetch = await client.FetchAllAsync(BasePriceApi, from, to, cancellationToken).ConfigureAwait(false);
        if (!fetch.Complete)
            logger.LogWarning("기초금액을 일부만 받았습니다 ({Got}/{Total}건). 나머지는 다음 수집 때 받습니다.", fetch.Items.Count, fetch.TotalCount);
        return [.. fetch.Items.Select(x => MapBasePrice(x, fetch.RequestParameter)).OfType<CollectedBasePrice>()];
    }

    /// <summary>
    /// A값 = 국민건강·국민연금·노인장기요양보험료 + 퇴직공제부금 + 산업안전보건관리비 + 안전관리비 (+ A값 대상인 품질관리비).
    /// 공고가 A값 산식을 쓰지 않으면(bidPrceCalclAYn ≠ Y) null. 항목 구성은 발주기관 공고문으로 다시 확인해야 한다.
    /// </summary>
    // 기초금액 원문의 비용 항목. A값 = 앞의 7개 합 (품질관리비는 qltyMngcstAObjYn=Y일 때만).
    private static readonly (string Field, string Label, bool InAValue)[] CostFields =
    [
        ("npnInsrprm", "국민연금보험료", true),
        ("mrfnHealthInsrprm", "국민건강보험료", true),
        ("odsnLngtrmrcprInsrprm", "노인장기요양보험료", true),
        ("rtrfundNon", "퇴직공제부금", true),
        ("sftyMngcst", "산업안전보건관리비", true),
        ("sftyChckMngcst", "안전관리비", true),
        ("qltyMngcst", "품질관리비", true),
        ("envCnsrvcst", "환경보전비", false),
        ("scontrctPayprcePayGrntyFee", "하도급대금 지급보증수수료", false),
    ];

    /// <summary>원문의 비용 항목을 A값 포함 여부와 함께 돌려준다. A값 미적용 공고는 모두 A값 밖으로 본다.</summary>
    public static IReadOnlyList<CostItem> CostItems(JsonElement row)
    {
        bool aApplies = Str(row, "bidPrceCalclAYn") == "Y";
        bool quality = Str(row, "qltyMngcstAObjYn") == "Y";
        return [.. CostFields.Select(f => new CostItem(f.Label, Long(row, f.Field),
            aApplies && f.InAValue && (f.Field != "qltyMngcst" || quality)))];
    }

    internal static CollectedBasePrice? MapBasePrice(JsonElement row, string requestParameter)
    {
        long basePrice = Long(row, "bssamt");
        if (basePrice <= 0) return null;

        long? aValue = Str(row, "bidPrceCalclAYn") == "Y" ? CostItems(row).Where(x => x.InAValue).Sum(x => x.Amount) : null;
        long netCost = Long(row, "bssAmtPurcnstcst");
        string begin = Str(row, "rsrvtnPrceRngBgnRate"), end = Str(row, "rsrvtnPrceRngEndRate");

        return new CollectedBasePrice
        {
            SourceSystem = SourceSystems.G2B,
            BusinessType = BusinessType.Construction,
            BidNo = Str(row, "bidNtceNo"),
            BidSeq = Str(row, "bidNtceOrd"),
            BasePrice = basePrice,
            AValue = aValue,
            NetCost = netCost > 0 ? netCost : null,
            OpenedAt = Date(row, "bssamtOpenDt"),
            ReservePriceRange = begin.Length > 0 && end.Length > 0 ? $"{begin}% ~ {end}%" : "",
            ApiName = BasePriceApi,
            RequestParameter = requestParameter,
            RawJson = JsonSerializer.Serialize(row, CollectService.ReadableJson),
        };
    }

    internal static CollectedNotice Map(JsonElement item, IReadOnlyList<JsonElement> licenseRows, string requestParameter)
    {
        string site = Str(item, "cnstrtsiteRgnNm");
        DateTime posted = Date(item, "bidNtceDt") ?? DateTime.MinValue;
        DateTime open = Date(item, "opengDt") ?? posted;
        DateTime due = Date(item, "bidClseDt") ?? open;
        string kind = Str(item, "ntceKindNm");

        return new CollectedNotice
        {
            SourceSystem = SourceSystems.G2B,
            BusinessType = BusinessType.Construction,
            BidNo = Str(item, "bidNtceNo"),
            BidSeq = Str(item, "bidNtceOrd"),
            Name = Str(item, "bidNtceNm"),
            Agency = Str(item, "ntceInsttNm"),
            DemandAgency = Str(item, "dminsttNm"),
            Region = RegionNames.ToShort(FirstWord(site)),
            SiteRegionName = site,
            ContractMethod = ContractMethods.Group(Str(item, "cntrctCnclsMthdNm")),
            License = Str(item, "mainCnsttyNm"),
            Licenses = [.. licenseRows.Select(r => new CollectedLicense(Int(r, "lmtGrpNo"), LicenseName(Str(r, "lcnsLmtNm")))).Where(l => l.Name.Length > 0)],
            DetailUrl = Str(item, "bidNtceDtlUrl"),
            BasePrice = null, // 기초금액은 별도 API(BsisAmount)로 받는다 (다음 단계)
            LowerLimitRate = double.TryParse(Str(item, "sucsfbidLwltRate"), NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : 0,
            PostedAt = posted,
            DueAt = due,
            OpenAt = open,
            IsRebid = Str(item, "reNtceYn") == "Y",
            IsCancelled = kind.Contains("취소", StringComparison.Ordinal),
            LastModifiedAt = Date(item, "chgDt") ?? posted,
            ApiName = ListApi,
            RequestParameter = requestParameter,
            RawJson = JsonSerializer.Serialize(new { notice = item, licenses = licenseRows }, CollectService.ReadableJson),
        };
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()).Trim() : "";

    private static long Long(JsonElement e, string name) =>
        long.TryParse(Str(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0;

    private static int Int(JsonElement e, string name) => int.TryParse(Str(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    private static DateTime? Date(JsonElement e, string name) =>
        DateTime.TryParseExact(Str(e, name), ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyyMMddHHmm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>"전기공사업/0037"처럼 붙어 오는 업종 코드를 뗀다.</summary>
    internal static string LicenseName(string raw) => System.Text.RegularExpressions.Regex.Replace(raw, @"/\d+$", "").Trim();

    private static string FirstWord(string s) => s.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
}
