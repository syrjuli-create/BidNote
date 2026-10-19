using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BidNote.Infrastructure.Persistence;

/// <summary>`dotnet ef migrations add` 때만 쓴다. 실제 실행 때 DB 위치는 설정 파일을 따른다.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BidNoteDbContext>
{
    public BidNoteDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BidNoteDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
