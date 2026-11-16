using IndieRadar.Application;
using IndieRadar.Domain.Entities;
using IndieRadar.Domain.Interfaces;
using IndieRadar.Infrastructure.AI;
using IndieRadar.Infrastructure.Collectors;
using IndieRadar.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// 配置读取：全部支持环境变量注入（Render / Neon / GitHub Actions 用）
// 约定大于配置：不填就是本地模式（内存 + Mock AI），零依赖可跑
// ============================================================

// 数据库模式：memory（默认，本地演示）| postgres（生产，Neon）
var dbMode = builder.Configuration["DB_MODE"] ?? "memory";
var connectionString = builder.Configuration["CONNECTION_STRING"] ?? string.Empty;
var isPostgres = string.Equals(dbMode, "postgres", StringComparison.OrdinalIgnoreCase);

// AI 模式：mock（默认，本地演示）| real（真实模型）
var llmMode = builder.Configuration["LLM_MODE"] ?? "mock";

// AI 三件套配置：主模型 + 兜底模型。默认值就是 Hy4 flash 主 / GLM-4-Flash 备，
// 只需注入 API_KEY 即可用；BaseUrl/ModelId 也都可覆盖，实现"动态换模型"。
var primaryLlm = new LlmProviderConfig
{
    Name = builder.Configuration["LLM_PRIMARY_NAME"] ?? "hy4-flash",
    Enabled = true,
    BaseUrl = builder.Configuration["LLM_PRIMARY_BASE_URL"] ?? "https://tokenhub.tencentmaas.com/v1",
    ApiKey = builder.Configuration["LLM_PRIMARY_API_KEY"] ?? string.Empty,
    ModelId = builder.Configuration["LLM_PRIMARY_MODEL_ID"] ?? "hy4-flash"
};
var fallbackLlm = new LlmProviderConfig
{
    Name = builder.Configuration["LLM_FALLBACK_NAME"] ?? "glm-4-flash",
    Enabled = true,
    BaseUrl = builder.Configuration["LLM_FALLBACK_BASE_URL"] ?? "https://open.bigmodel.cn/api/paas/v4",
    ApiKey = builder.Configuration["LLM_FALLBACK_API_KEY"] ?? string.Empty,
    ModelId = builder.Configuration["LLM_FALLBACK_MODEL_ID"] ?? "glm-4-flash",
    IsFallback = true
};

// ===== CORS：允许前端（小程序 / H5 / Android）跨域调用 =====
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
});

// ===== HttpClient：供采集器和 AI 调用 =====
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("rss", c => ConfigureHttp(c));
builder.Services.AddHttpClient("hn", c => ConfigureHttp(c));
builder.Services.AddHttpClient("llm", c => ConfigureHttp(c));

// ===== 依赖注入：仓储（按 DB_MODE 切换内存 / Postgres）=====
if (isPostgres)
{
    builder.Services.AddSingleton<IArticleRepository>(_ => new PostgresArticleRepository(connectionString));
    builder.Services.AddSingleton<IDailyDigestRepository>(_ => new PostgresDigestRepository(connectionString));
    builder.Services.AddSingleton<ISourceRepository>(_ => new PostgresSourceRepository(connectionString));
    builder.Services.AddSingleton(new PostgresInitializer(connectionString));
    builder.Services.AddSingleton(new PostgresSeeder(connectionString));
}
else
{
    builder.Services.AddSingleton<IArticleRepository, InMemoryArticleRepository>();
    builder.Services.AddSingleton<IDailyDigestRepository, InMemoryDigestRepository>();
    builder.Services.AddSingleton<ISourceRepository, InMemorySourceRepository>();
}

// ===== 依赖注入：采集器 =====
builder.Services.AddSingleton<IArticleCollector, SampleCollector>();
builder.Services.AddSingleton<IArticleCollector>(sp =>
    new RssCollector(sp.GetRequiredService<IHttpClientFactory>().CreateClient("rss"),
        sp.GetService<ILogger<RssCollector>>()));
builder.Services.AddSingleton<IArticleCollector>(sp =>
    new HackerNewsCollector(sp.GetRequiredService<IHttpClientFactory>().CreateClient("hn"),
        sp.GetService<ILogger<HackerNewsCollector>>()));

// ===== 依赖注入：AI 与流水线 =====
builder.Services.AddSingleton<LlmProviderFactory>();
builder.Services.AddSingleton<ILLMProvider>(sp =>
    sp.GetRequiredService<LlmProviderFactory>().Create(llmMode, primaryLlm, fallbackLlm));
builder.Services.AddSingleton<CoarseFilter>();
builder.Services.AddSingleton<AiEnrichmentService>(sp =>
    new AiEnrichmentService(
        sp.GetRequiredService<ILLMProvider>(),
        scoreThreshold: builder.Configuration.GetValue<int>("AI_SCORE_THRESHOLD", 60)));
builder.Services.AddSingleton<DigestPipeline>();

// ===== HttpClient 通用配置 =====
static void ConfigureHttp(HttpClient c)
{
    c.Timeout = TimeSpan.FromSeconds(20);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("IndieRadar/1.0");
}

var app = builder.Build();
app.UseCors();

// ===== 启动时初始化 Postgres（建表 + 灌种子源），幂等 =====
if (isPostgres)
{
    using (var scope = app.Services.CreateScope())
    {
        var initializer = scope.ServiceProvider.GetRequiredService<PostgresInitializer>();
        var seeder = scope.ServiceProvider.GetRequiredService<PostgresSeeder>();
        await initializer.InitializeAsync();
        await seeder.SeedAsync();
    }
}

// ===== 健康检查（同时暴露当前运行模式，便于验证切换是否生效）=====
app.MapGet("/", () => new
{
    service = "IndieRadar API",
    status = "running",
    dbMode,
    llmMode,
    llmProvider = llmMode == "real" ? $"{primaryLlm.Name} → {fallbackLlm.Name}" : "mock",
    time = DateTimeOffset.UtcNow
});

// ===== 触发一天日报的生成（手动演示 / GitHub Actions 定时调用）=====
app.MapPost("/api/digest/run", async (
    DigestPipeline pipeline,
    DateTimeOffset? date,
    CancellationToken ct) =>
{
    var target = date ?? DateTimeOffset.UtcNow;
    var result = await pipeline.RunAsync(target, ct);
    return Results.Ok(result);
});

// ===== 获取指定日期的日报（前端首屏）=====
app.MapGet("/api/digest/{date}", async (
    string date,
    IDailyDigestRepository digests,
    IArticleRepository articles,
    CancellationToken ct) =>
{
    var digest = await digests.GetAsync(date, ct);
    if (digest is null)
        return Results.NotFound(new { message = $"没有 {date} 的日报，请先触发生成" });

    var all = await articles.GetAllAsync(ct);
    var map = all.ToDictionary(a => a.Id);
    var items = digest.ArticleIds
        .Where(map.ContainsKey)
        .Select(id => map[id])
        .ToList();

    return Results.Ok(new
    {
        digest.Date,
        digest.GeneratedAt,
        digest.TotalCollected,
        digest.PassedCoarseCount,
        digest.PassedAiCount,
        articles = items
    });
});

// ===== 单篇文章详情（详情页用 id 查询）=====
// 路径用 /detail/{id} 而非 /{id}，避免与 /api/articles/category/{category} 产生路由歧义
app.MapGet("/api/articles/detail/{id}", async (
    string id,
    IArticleRepository articles,
    CancellationToken ct) =>
{
    var all = await articles.GetAllAsync(ct);
    var article = all.FirstOrDefault(a => a.Id == id);
    return article is null
        ? Results.NotFound(new { message = "文章不存在" })
        : Results.Ok(article);
});

// ===== 按分类浏览（前端分类筛选）=====
app.MapGet("/api/articles/category/{category}", async (
    string category,
    IArticleRepository articles,
    CancellationToken ct) =>
{
    var all = await articles.GetAllAsync(ct);
    var items = all
        .Where(a => a.PassedAiFilter &&
                    string.Equals(a.Category, category, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(a => a.RelevanceScore)
        .ToList();
    return Results.Ok(items);
});

// ===== 查看所有历史日报列表 =====
app.MapGet("/api/digest/history", async (
    IDailyDigestRepository digests,
    CancellationToken ct) =>
{
    var list = await digests.GetRecentAsync(30, ct);
    return Results.Ok(list.Select(d => new { d.Date, d.PassedAiCount, d.GeneratedAt }));
});

app.Run();
