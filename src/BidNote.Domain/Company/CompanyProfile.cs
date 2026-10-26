using BidNote.Domain.Notices;

namespace BidNote.Domain.Company;

/// <summary>우리 회사 정보. 혼자 쓰는 앱이라 한 줄(Id=1)만 둔다. 주민등록번호 같은 개인 식별 정보는 받지 않는다.</summary>
public class CompanyProfile
{
    public const int SingletonId = 1;
    public const int MaxNameLength = 100;
    public const int MaxBizNoLength = 20;
    public const int MaxEmailLength = 200;

    public int Id { get; set; } = SingletonId;
    public string Name { get; set; } = "";
    public string BizNo { get; set; } = "";

    /// <summary>소재지 시도 (짧은 이름, 예: 대전).</summary>
    public string Region { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

/// <summary>우리 회사가 가진 면허(업종) 하나. 이름은 나라장터 면허 제한 이름과 같게 쓴다 (예: 지반조성ㆍ포장공사업).</summary>
public class CompanyLicense
{
    public const int MaxNameLength = 100;

    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime AddedAt { get; set; }

    /// <summary>
    /// 이 업종의 시공능력평가액(원). 참고용 — 시평액은 공고문에 따로 제한이 있을 때만 참여를 막으므로 판정에 쓰지 않는다.
    /// </summary>
    public long? CapacityAmount { get; set; }

    /// <summary>
    /// 이 업종의 최근 5년 시공 실적 합계(원). 적격심사 시공경험 점수를 예상하는 데 쓴다 (실적이 모자라도 입찰은 할 수 있다).
    /// </summary>
    public long? PerformanceAmount { get; set; }

    /// <summary>이 업종의 주력분야 (예: 포장공사). '|'로 잇는다. 비어 있으면 주력분야는 따지지 않는다.</summary>
    public string MainFields { get; set; } = "";

    public IReadOnlyList<string> MainFieldNames => NoticeLicense.SplitAlternatives(MainFields);
}

public enum MainFieldVerdict
{
    /// <summary>공고에 주력분야 제한이 없다.</summary>
    NoLimit,
    Within,

    /// <summary>내가 넣은 주력분야와 다르다. 참고용이라 참여 가능에서 빼지는 않는다.</summary>
    Mismatch,

    /// <summary>그 면허의 주력분야를 넣지 않아 판단할 수 없다.</summary>
    Unknown,
}

/// <summary>
/// 주력분야 판정 (참고용). 충족한 면허 그룹에서 내 면허 이름 그대로 충족한 줄만 본다 (대체 업종으로 충족한 줄은 따지지 않는다).
/// 줄의 주력분야 묶음 중 하나의 분야를 모두 내 주력분야에 갖고 있으면 맞다.
/// 공고 대부분(2026-10-25 기준 3,197건 중 3,005건)이 주력분야 평가 여부 N이라 참가 자격 제한인지 확실하지 않아 빼지 않는다.
/// </summary>
public static class MainFieldEligibility
{
    public static MainFieldVerdict Check(IEnumerable<LicenseRow> groupRows, IReadOnlyDictionary<string, IReadOnlyCollection<string>> myMainFields)
    {
        ArgumentNullException.ThrowIfNull(groupRows);
        ArgumentNullException.ThrowIfNull(myMainFields);
        bool any = false, unknown = false;
        foreach (var row in groupRows.Where(r => r.MainFieldGroups.Count > 0 && myMainFields.ContainsKey(r.Name)))
        {
            any = true;
            var mine = myMainFields[row.Name];
            if (mine.Count == 0)
            {
                unknown = true;
                continue;
            }
            if (!row.MainFieldGroups.Any(g => g.All(mine.Contains))) return MainFieldVerdict.Mismatch;
        }
        return !any ? MainFieldVerdict.NoLimit : unknown ? MainFieldVerdict.Unknown : MainFieldVerdict.Within;
    }
}

/// <summary>본사·지사 소재지 하나. 나라장터 지역 이름 그대로 ("충청북도 충주시" 또는 시도만 "충청북도").</summary>
public class CompanyLocation
{
    public const int MaxNameLength = 100;

    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsHeadOffice { get; set; }
    public DateTime AddedAt { get; set; }
}

public enum RegionVerdict
{
    /// <summary>공고에 참가 가능 지역 제한이 없다 (또는 아직 받지 않았다).</summary>
    NoLimit,
    Within,
    Outside,

    /// <summary>판단할 수 없다 (소재지를 넣지 않았거나 시도만 넣었는데 공고가 시군구로 제한).</summary>
    Unknown,
}

/// <summary>참가 가능 지역 판정. 공고의 지역 중 한 곳에 내 소재지(본사·지사) 중 하나가 들어가면 된다.</summary>
public static class RegionEligibility
{
    public static RegionVerdict Check(IReadOnlyCollection<string> noticeRegions, IReadOnlyCollection<string> myLocations)
    {
        ArgumentNullException.ThrowIfNull(noticeRegions);
        ArgumentNullException.ThrowIfNull(myLocations);
        var limits = noticeRegions.Select(Key).Where(x => x.Province.Length > 0).ToList();
        if (limits.Count == 0) return RegionVerdict.NoLimit;
        var mine = myLocations.Select(Key).Where(x => x.Province.Length > 0).ToList();
        if (mine.Count == 0) return RegionVerdict.Unknown;

        bool unknown = false;
        foreach (var m in mine)
        {
            foreach (var l in limits)
            {
                if (m.Province != l.Province) continue;
                if (l.District.Length == 0 || l.District == m.District) return RegionVerdict.Within;
                if (m.District.Length == 0) unknown = true; // 시도만 넣었는데 공고는 시군구로 제한
            }
        }
        return unknown ? RegionVerdict.Unknown : RegionVerdict.Outside;
    }

    /// <summary>"강원도 원주시"와 "강원특별자치도 원주시"를 같게 보도록 시도는 짧은 이름으로 바꾼다.</summary>
    private static (string Province, string District) Key(string name)
    {
        string[] parts = (name ?? "").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? ("", "") : (RegionNames.ToShort(parts[0]), parts.Length > 1 ? parts[1] : "");
    }
}

/// <summary>면허 제한 한 줄: 이 면허 또는 대체 인정 업종 중 하나가 있으면 충족한다. 주력분야 묶음은 참고용.</summary>
public sealed record LicenseRow(int GroupNo, string Name, IReadOnlyList<string> Alternatives, IReadOnlyList<IReadOnlyList<string>>? MainFields = null)
{
    public IReadOnlyList<IReadOnlyList<string>> MainFieldGroups => MainFields ?? [];
}

public enum EligibilityKind
{
    /// <summary>공고에 면허 제한 정보가 없어 판단할 수 없다.</summary>
    Unknown,
    Eligible,
    NotEligible,
}

/// <param name="GroupNo">충족한 그룹, 또는 가장 가까운(빠진 면허가 가장 적은) 그룹.</param>
/// <param name="Missing">그 그룹에서 빠진 면허. 참여 가능이면 비어 있다.</param>
/// <param name="Region">참가 가능 지역 판정. 면허 판정과 따로 채운다.</param>
/// <param name="MainField">주력분야 판정 (참고용, 참여 가능 여부에는 넣지 않는다).</param>
public sealed record Eligibility(EligibilityKind Kind, int? GroupNo, IReadOnlyList<string> Missing,
    RegionVerdict Region = RegionVerdict.NoLimit, MainFieldVerdict MainField = MainFieldVerdict.NoLimit)
{
    /// <summary>
    /// 면허가 맞고 지역 밖이 아니다 (모르는 것은 빼지 않는다). 참가 자격은 면허·지역으로만 본다 —
    /// 실적은 적격심사 점수(시공경험)를 정할 뿐 참여를 막지 않는다 (실적제한 공고는 공고문 조건을 따로 확인).
    /// </summary>
    public bool CanBid => Kind == EligibilityKind.Eligible && Region != RegionVerdict.Outside;
}

/// <summary>
/// 면허로 참여할 수 있는지 판정한다. 그룹 하나의 모든 줄을 충족하면 참여 가능 (그룹끼리는 "또는", 그룹 안은 "그리고").
/// 주력분야(indstrytyMfrcFldList)와 금액(시평액·실적)은 따지지 않는다.
/// </summary>
public static class LicenseEligibility
{
    public static Eligibility Check(IEnumerable<LicenseRow> rows, IReadOnlySet<string> myLicenses)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(myLicenses);
        var groups = rows.Where(r => r.Name.Length > 0).GroupBy(r => r.GroupNo).OrderBy(g => g.Key).ToList();
        if (groups.Count == 0) return new Eligibility(EligibilityKind.Unknown, null, []);

        var evaluated = groups
            .Select(g => (Group: g.Key, Missing: g.Where(r => Matches(r, myLicenses).Count == 0).Select(r => r.Name).ToList()))
            .ToList();
        foreach (var (group, missing) in evaluated)
        {
            if (missing.Count == 0) return new Eligibility(EligibilityKind.Eligible, group, []);
        }
        var nearest = evaluated.OrderBy(x => x.Missing.Count).ThenBy(x => x.Group).First();
        return new Eligibility(EligibilityKind.NotEligible, nearest.Group, nearest.Missing);
    }

    /// <summary>면허 제한 한 줄을 충족하는 내 면허 (줄의 면허 또는 대체 인정 업종).</summary>
    public static IReadOnlyList<string> Matches(LicenseRow row, IReadOnlySet<string> mine)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(mine);
        return [.. row.Alternatives.Prepend(row.Name).Where(mine.Contains).Distinct(StringComparer.Ordinal)];
    }
}
