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

    public static string JoinAlternatives(IEnumerable<string> names) =>
        string.Join(Separator, names.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal));

    public static IReadOnlyList<string> SplitAlternatives(string? joined) =>
        string.IsNullOrEmpty(joined) ? [] : joined.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
