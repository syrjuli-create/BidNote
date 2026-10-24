using BidNote.Domain.Company;

namespace BidNote.Application.Company;

public sealed record CompanyInfo(string Name, string BizNo, string Region, string Email, IReadOnlyList<string> Licenses)
{
    public bool HasLicenses => Licenses.Count > 0;
}

public sealed record CompanyProfileInput(string Name, string BizNo, string Region, string Email);

/// <summary>면허 고르기 목록 한 줄. 진행 중 공고의 면허 제한(대체 인정 업종 포함)에 나온 횟수.</summary>
public sealed record LicenseOption(string Name, int OpenNoticeCount);

/// <summary>진행 중 공고를 내 면허로 나눈 수. Unknown은 면허 제한 정보가 없는 공고.</summary>
public sealed record EligibilitySummary(int Eligible, int NotEligible, int Unknown);

public interface ICompanyStore
{
    Task<CompanyInfo> GetAsync(CancellationToken cancellationToken);

    /// <summary>회사 정보를 저장한다. 앞뒤 공백을 떼고 길면 자른다.</summary>
    Task SaveProfileAsync(CompanyProfileInput input, CancellationToken cancellationToken);

    /// <summary>면허를 더한다. 이름이 비었거나 이미 있으면 false.</summary>
    Task<bool> AddLicenseAsync(string name, CancellationToken cancellationToken);

    Task RemoveLicenseAsync(string name, CancellationToken cancellationToken);

    /// <summary>고를 수 있는 면허 이름. 진행 중 공고에 많이 나온 순.</summary>
    Task<IReadOnlyList<LicenseOption>> LicenseOptionsAsync(DateTime now, CancellationToken cancellationToken);

    /// <summary>진행 중 공고를 내 면허로 나눈 수. 면허를 등록하지 않았으면 모두 0.</summary>
    Task<EligibilitySummary> SummaryAsync(DateTime now, CancellationToken cancellationToken);

    /// <summary>공고 하나를 내 면허로 판정한다. 면허를 등록하지 않았으면 null.</summary>
    Task<Eligibility?> CheckAsync(int noticeId, CancellationToken cancellationToken);
}
