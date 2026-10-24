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
}

/// <summary>면허 제한 한 줄: 이 면허 또는 대체 인정 업종 중 하나가 있으면 충족한다.</summary>
public sealed record LicenseRow(int GroupNo, string Name, IReadOnlyList<string> Alternatives);

public enum EligibilityKind
{
    /// <summary>공고에 면허 제한 정보가 없어 판단할 수 없다.</summary>
    Unknown,
    Eligible,
    NotEligible,
}

/// <param name="GroupNo">충족한 그룹, 또는 가장 가까운(빠진 면허가 가장 적은) 그룹.</param>
/// <param name="Missing">그 그룹에서 빠진 면허. 참여 가능이면 비어 있다.</param>
public sealed record Eligibility(EligibilityKind Kind, int? GroupNo, IReadOnlyList<string> Missing);

/// <summary>
/// 면허로 참여할 수 있는지 판정한다. 그룹 하나의 모든 줄을 충족하면 참여 가능 (그룹끼리는 "또는", 그룹 안은 "그리고").
/// 주력분야(indstrytyMfrcFldList)는 따지지 않는다.
/// </summary>
public static class LicenseEligibility
{
    public static Eligibility Check(IEnumerable<LicenseRow> rows, IReadOnlySet<string> myLicenses)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(myLicenses);
        var groups = rows.Where(r => r.Name.Length > 0).GroupBy(r => r.GroupNo).OrderBy(g => g.Key).ToList();
        if (groups.Count == 0) return new Eligibility(EligibilityKind.Unknown, null, []);

        var best = groups
            .Select(g => (Group: g.Key, Missing: g.Where(r => !Satisfied(r, myLicenses)).Select(r => r.Name).ToList()))
            .OrderBy(x => x.Missing.Count).ThenBy(x => x.Group)
            .First();
        return best.Missing.Count == 0
            ? new Eligibility(EligibilityKind.Eligible, best.Group, [])
            : new Eligibility(EligibilityKind.NotEligible, best.Group, best.Missing);
    }

    private static bool Satisfied(LicenseRow row, IReadOnlySet<string> mine) =>
        mine.Contains(row.Name) || row.Alternatives.Any(mine.Contains);
}
