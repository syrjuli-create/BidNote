using BidNote.Application.Collecting;
using BidNote.Application.Notices;
using BidNote.Domain.Collecting;
using BidNote.Domain.Notices;
using BidNote.Infrastructure.Persistence;

namespace BidNote.Tests;

public sealed class NoticeDetailTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 9, 0, 0);
    private readonly TestDb testDb = new();

    public void Dispose() => testDb.Dispose();

    private static Notice NewNotice(string seq, DateTime due) => new()
    {
        SourceSystem = SourceSystems.G2B,
        BusinessType = BusinessType.Construction,
        BidNo = "R1",
        BidSeq = seq,
        Name = "○○초 화장실 개선공사",
        Agency = "○○교육지원청",
        DueAt = due,
        OpenAt = due.AddHours(1),
        PostedAt = Now.AddDays(-3),
        LowerLimitRate = 87.745,
        LastModifiedAt = Now,
        FirstCollectedAt = Now,
        LastCheckedAt = Now,
    };

    [Fact]
    public void 정정_이력은_바뀐_항목만_전후로_보여주고_최근_것이_먼저다()
    {
        var first = NewNotice("000", Now.AddDays(1));
        var second = NewNotice("001", Now.AddDays(3));
        var current = NewNotice("002", Now.AddDays(3));
        current.Name = "○○초 화장실 개선공사(재)";

        var changes = NoticeHistoryDiff.Build(
            [(Now.AddDays(-2), NoticeChangeType.Corrected, CollectService.SnapshotJson(first)),
             (Now.AddDays(-1), NoticeChangeType.Corrected, CollectService.SnapshotJson(second))],
            CollectService.SnapshotJson(current));

        Assert.Equal(2, changes.Count);
        Assert.Equal(("001", "002"), (changes[0].FromSeq, changes[0].ToSeq));
        Assert.Equal([new FieldChange("공고명", "○○초 화장실 개선공사", "○○초 화장실 개선공사(재)")], changes[0].Fields);
        Assert.Equal(
            [new FieldChange("입찰 마감", "2026-10-20 09:00", "2026-10-22 09:00"), new FieldChange("개찰 일시", "2026-10-20 10:00", "2026-10-22 10:00")],
            changes[1].Fields);
    }

    [Fact]
    public async Task 상세는_면허를_그룹별로_묶고_원문에서_추가_정보를_꺼낸다()
    {
        int id;
        using (var db = testDb.NewContext())
        {
            var n = NewNotice("000", Now.AddDays(2));
            n.Licenses = [new() { GroupNo = 1, Name = "실내건축공사업" }, new() { GroupNo = 1, Name = "건축공사업" }, new() { GroupNo = 2, Name = "전기공사업" }];
            db.Notices.Add(n);
            db.ApiRawResponses.Add(new ApiRawResponse
            {
                SourceSystem = SourceSystems.G2B,
                BusinessType = BusinessType.Construction,
                BidNo = "R1",
                ApiName = "getBidPblancListInfoCnstwk",
                ResponseJson = """{"notice":{"cntrctCnclsMthdNm":"제한경쟁","presmptPrce":"169454545","bdgtAmt":"0","opengPlce":""},"licenses":[]}""",
            });
            await db.SaveChangesAsync();
            id = n.Id;
        }

        using var ctx = testDb.NewContext();
        var d = await new NoticeDetailQueries(ctx).GetAsync(id, CancellationToken.None);

        Assert.NotNull(d);
        Assert.Equal([new[] { "실내건축공사업", "건축공사업" }, new[] { "전기공사업" }], d.Licenses.Select(g => g.Names.ToArray()));
        // 0원·빈 값은 빼고, 금액은 원 단위로
        Assert.Equal([new LabeledValue("계약방법", "제한경쟁"), new LabeledValue("추정가격", "169,454,545원")], d.Extras);
        Assert.Empty(d.Changes);
    }

    [Fact]
    public async Task 없는_공고는_null()
    {
        using var ctx = testDb.NewContext();
        Assert.Null(await new NoticeDetailQueries(ctx).GetAsync(999, CancellationToken.None));
    }
}
