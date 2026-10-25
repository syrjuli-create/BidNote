using BidNote.Domain.Company;

namespace BidNote.Application.Company;

/// <param name="AmountLimit">시공능력평가액과 "실적 ÷ 배수" 중 작은 값. 넣은 값이 없으면 null.</param>
public sealed record CompanyLicenseInfo(string Name, long? CapacityAmount, long? PerformanceAmount, double? PerformanceMultiple, long? AmountLimit)
{
    /// <summary>이 업종의 주력분야. 비어 있으면 주력분야는 판단하지 않는다.</summary>
    public IReadOnlyList<string> MainFields { get; init; } = [];
}

/// <summary>본사·지사 소재지 하나 (나라장터 지역 이름 그대로).</summary>
public sealed record CompanyLocationInfo(string Name, bool IsHeadOffice);

public sealed record CompanyInfo(string Name, string BizNo, string Region, string Email, IReadOnlyList<CompanyLicenseInfo> Licenses)
{
    public bool HasLicenses => Licenses.Count > 0;

    public IReadOnlyList<string> LicenseNames => [.. Licenses.Select(x => x.Name)];

    /// <summary>본사가 앞에 온다.</summary>
    public IReadOnlyList<CompanyLocationInfo> Locations { get; init; } = [];
}

/// <summary>소재지 고르기 목록 한 줄. 진행 중 공고의 참가 가능 지역에 나온 횟수.</summary>
public sealed record LocationOption(string Name, int OpenNoticeCount);

public sealed record CompanyProfileInput(string Name, string BizNo, string Region, string Email);

/// <summary>면허 하나의 금액 정보 (원, 배수). 비운 값은 null.</summary>
public sealed record LicenseAmountsInput(long? CapacityAmount, long? PerformanceAmount, double? PerformanceMultiple);

/// <summary>면허 고르기 목록 한 줄. 진행 중 공고의 면허 제한(대체 인정 업종 포함)에 나온 횟수.</summary>
public sealed record LicenseOption(string Name, int OpenNoticeCount);

/// <summary>
/// 진행 중 공고를 내 면허·금액으로 나눈 수. Eligible은 면허가 맞고 금액 상한도 넘지 않는 공고(상한을 모르면 면허만 봄),
/// OverLimit은 면허는 맞지만 추정가격이 상한을 넘는 공고, OutsideRegion은 면허·금액은 맞지만 참가 가능 지역 밖인 공고,
/// Unknown은 면허 제한 정보가 없는 공고. RegionUnknown은 참여 가능(Eligible) 중 지역을 판단하지 못한 공고 수(소재지 확인 필요).
/// </summary>
/// MainFieldMismatch는 참여 가능 중 주력분야가 내 주력분야와 다른 공고 수(참고용, 빼지 않음).
public sealed record EligibilitySummary(int Eligible, int NotEligible, int Unknown, int OverLimit = 0, int OutsideRegion = 0, int RegionUnknown = 0,
    int MainFieldMismatch = 0);

public interface ICompanyStore
{
    Task<CompanyInfo> GetAsync(CancellationToken cancellationToken);

    /// <summary>회사 정보를 저장한다. 앞뒤 공백을 떼고 길면 자른다.</summary>
    Task SaveProfileAsync(CompanyProfileInput input, CancellationToken cancellationToken);

    /// <summary>면허를 더한다. 이름이 비었거나 이미 있으면 false.</summary>
    Task<bool> AddLicenseAsync(string name, CancellationToken cancellationToken);

    Task RemoveLicenseAsync(string name, CancellationToken cancellationToken);

    /// <summary>면허 하나의 시공능력평가액·실적·실적 배수를 저장한다. 0 이하는 비운 것으로 본다. 없는 면허면 false.</summary>
    Task<bool> SaveLicenseAmountsAsync(string name, LicenseAmountsInput input, CancellationToken cancellationToken);

    /// <summary>면허 하나의 주력분야를 저장한다 (앞뒤 공백·빈 값·중복은 뺀다). 없는 면허면 false.</summary>
    Task<bool> SaveLicenseMainFieldsAsync(string name, IEnumerable<string> mainFields, CancellationToken cancellationToken);

    /// <summary>면허별로 진행 공고에 나온 주력분야 이름 (고르기 목록).</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> MainFieldOptionsAsync(DateTime now, CancellationToken cancellationToken);

    /// <summary>소재지를 더한다. 이름이 비었거나 이미 있으면 false. 본사로 넣으면 다른 소재지의 본사 표시는 뗀다.</summary>
    Task<bool> AddLocationAsync(string name, bool isHeadOffice, CancellationToken cancellationToken);

    Task RemoveLocationAsync(string name, CancellationToken cancellationToken);

    /// <summary>고를 수 있는 소재지 이름 (진행 중 공고의 참가 가능 지역에 나온 이름). 많이 나온 순.</summary>
    Task<IReadOnlyList<LocationOption>> LocationOptionsAsync(DateTime now, CancellationToken cancellationToken);

    /// <summary>고를 수 있는 면허 이름. 진행 중 공고에 많이 나온 순.</summary>
    Task<IReadOnlyList<LicenseOption>> LicenseOptionsAsync(DateTime now, CancellationToken cancellationToken);

    /// <summary>진행 중 공고를 내 면허·금액·지역으로 나눈 수. 면허를 등록하지 않았으면 모두 0.</summary>
    Task<EligibilitySummary> SummaryAsync(DateTime now, CancellationToken cancellationToken);

    /// <summary>공고 하나를 내 면허·금액으로 판정한다. 면허를 등록하지 않았으면 null.</summary>
    Task<Eligibility?> CheckAsync(int noticeId, CancellationToken cancellationToken);
}
