namespace BidNote.Domain.Notices;

/// <summary>나라장터 현장 지역의 시도 이름을 필터에 쓰기 좋은 짧은 이름으로 바꾼다. 원래 이름은 SiteRegionName에 남는다.</summary>
public static class RegionNames
{
    private static readonly Dictionary<string, string> Short = new(StringComparer.Ordinal)
    {
        ["서울특별시"] = "서울",
        ["부산광역시"] = "부산",
        ["대구광역시"] = "대구",
        ["인천광역시"] = "인천",
        ["광주광역시"] = "광주",
        ["대전광역시"] = "대전",
        ["울산광역시"] = "울산",
        ["세종특별자치시"] = "세종",
        ["경기도"] = "경기",
        ["강원도"] = "강원",
        ["강원특별자치도"] = "강원",
        ["충청북도"] = "충북",
        ["충청남도"] = "충남",
        ["전라북도"] = "전북",
        ["전북특별자치도"] = "전북",
        ["전라남도"] = "전남",
        ["경상북도"] = "경북",
        ["경상남도"] = "경남",
        ["제주특별자치도"] = "제주",
        ["전남광주통합특별시"] = "전남·광주",
    };

    /// <summary>모르는 이름은 그대로 둔다.</summary>
    public static string ToShort(string name)
    {
        string s = (name ?? "").Trim();
        return Short.TryGetValue(s, out var v) ? v : s;
    }

    /// <summary>짧은 이름으로 바꿔야 하는 긴 이름들 (이미 저장된 공고를 고칠 때 쓴다).</summary>
    public static IEnumerable<string> LongNames => Short.Keys;
}
