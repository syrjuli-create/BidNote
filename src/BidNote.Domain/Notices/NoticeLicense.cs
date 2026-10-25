namespace BidNote.Domain.Notices;

/// <summary>
/// 공고의 면허(업종) 제한 한 줄. 같은 그룹 번호끼리는 "그리고"(모두 필요), 다른 그룹끼리는 "또는"(그중 한 그룹만 갖추면 됨)으로 본다.
/// 2026-10-25 실제 데이터로 확인: 그룹1 토목건축공사업 / 그룹2 건축공사업, 한 그룹 안에 일반소방(전기)+일반소방(기계) 같은 모양이다.
/// </summary>
public class NoticeLicense
{
    /// <summary><see cref="Alternatives"/>를 이어 붙일 때 쓰는 구분자.</summary>
    public const char Separator = '|';

    public int Id { get; set; }
    public int NoticeId { get; set; }
    public Notice? Notice { get; set; }

    public int GroupNo { get; set; }
    public string Name { get; set; } = "";

    /// <summary>
    /// 이 줄 대신 인정하는 업종 (원문 permsnIndstrytyList, 예: 건축공사업 줄에 토목건축공사업). '|'로 잇는다.
    /// null이면 아직 원문에서 읽지 않은 것이다 (시작할 때 채운다).
    /// </summary>
    public string? Alternatives { get; set; }

    public IReadOnlyList<string> AlternativeNames => SplitAlternatives(Alternatives);

    /// <summary>
    /// 주력분야 제한 (원문 indstrytyMfrcFldList, 예: "[1^포장공사]", "[1^토공사^포장공사]", "[1^도장공사],[2^습식·방수공사]").
    /// 대괄호 하나를 묶음으로 보고 '|'로, 묶음 안 분야는 '^'로 잇는다. null이면 아직 원문에서 읽지 않은 것이다.
    /// 묶음 안은 "모두", 묶음끼리는 "하나"로 본다 (면허 그룹과 같은 모양으로 가정, 확인 필요).
    /// </summary>
    public string? MainFields { get; set; }

    public IReadOnlyList<IReadOnlyList<string>> MainFieldGroups => SplitMainFields(MainFields);

    public static string JoinMainFields(IEnumerable<IReadOnlyList<string>> groups) =>
        string.Join(Separator, groups.Select(g => string.Join('^', g.Select(x => x.Trim()).Where(x => x.Length > 0))).Where(x => x.Length > 0));

    public static IReadOnlyList<IReadOnlyList<string>> SplitMainFields(string? joined) =>
        string.IsNullOrEmpty(joined) ? []
            : [.. joined.Split(Separator, StringSplitOptions.RemoveEmptyEntries)
                .Select(g => (IReadOnlyList<string>)g.Split('^', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];

    public static string JoinAlternatives(IEnumerable<string> names) =>
        string.Join(Separator, names.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal));

    public static IReadOnlyList<string> SplitAlternatives(string? joined) =>
        string.IsNullOrEmpty(joined) ? [] : joined.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
