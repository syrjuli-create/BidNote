namespace BidNote.Domain.Notices;

/// <summary>공고의 면허(업종) 제한 한 줄. 같은 그룹 번호끼리는 "또는", 다른 그룹끼리는 "그리고"로 본다 (실제 데이터로 확인 필요).</summary>
public class NoticeLicense
{
    public int Id { get; set; }
    public int NoticeId { get; set; }
    public Notice? Notice { get; set; }

    public int GroupNo { get; set; }
    public string Name { get; set; } = "";
}
