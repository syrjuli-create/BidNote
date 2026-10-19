using BidNote.Infrastructure;
using BidNote.Infrastructure.Persistence;
using BidNote.Web.Components;
using BidNote.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// 실제 API 키 등 PC별 설정은 appsettings.Local.json에 둔다 (예시: appsettings.Local.example.json).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
// 환경변수·명령줄 설정이 Local 파일보다 우선하도록 다시 붙인다 (예: --G2B:UseFakeData=false로 잠깐 실제 모드 시험).
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddBidNoteInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddSingleton<CollectorState>();
builder.Services.AddSingleton<CollectorSignal>();
builder.Services.AddHostedService<CollectorHostedService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var sp = scope.ServiceProvider;
    bool realMode = !builder.Configuration.GetValue("G2B:UseFakeData", true);
    await DatabaseInitializer.InitializeAsync(sp.GetRequiredService<BidNoteDbContext>(), sp.GetRequiredService<StorageLocation>(),
        purgeFakeData: realMode, sp.GetRequiredService<ILoggerFactory>().CreateLogger("Database"), CancellationToken.None);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
// 시작 화면(실행파일\입찰노트.html)이 서버가 켜졌는지 확인할 때 쓴다.
app.MapGet("/health", () => Results.Text("ok"));
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
