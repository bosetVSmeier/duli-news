using System.Text.Json;
using IndieRadar.Domain.Entities;
using IndieRadar.Domain.Interfaces;
using Npgsql;

namespace IndieRadar.Infrastructure.Persistence;

/// <summary>
/// Postgres 数据库初始化器。负责在启动时自动建表（IF NOT EXISTS，幂等），
/// 这样本地 Docker 或 Neon 首次连接就能直接用，不用手动执行 SQL 脚本。
/// </summary>
public sealed class PostgresInitializer
{
    private readonly string _connectionString;

    public PostgresInitializer(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>建表。表结构与 Article / DailyDigest / Source 实体字段一一对应。</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var ddl = """
            -- 资讯来源表
            CREATE TABLE IF NOT EXISTS sources (
                id          TEXT PRIMARY KEY,
                name        TEXT NOT NULL,
                kind        TEXT NOT NULL,
                url         TEXT NOT NULL,
                weight      INTEGER NOT NULL DEFAULT 5,
                enabled     BOOLEAN NOT NULL DEFAULT TRUE
            );

            -- 文章表。Id 用 URL 哈希，天然幂等去重。
            CREATE TABLE IF NOT EXISTS articles (
                id                    TEXT PRIMARY KEY,
                source_id             TEXT NOT NULL,
                original_title        TEXT NOT NULL,
                url                   TEXT NOT NULL,
                summary               TEXT NOT NULL DEFAULT '',
                published_at          TIMESTAMPTZ NOT NULL,
                collected_at          TIMESTAMPTZ NOT NULL,
                passed_coarse_filter  BOOLEAN NOT NULL DEFAULT FALSE,
                coarse_filter_reason  TEXT NOT NULL DEFAULT '',
                relevance_score       INTEGER NULL,
                chinese_title         TEXT NULL,
                chinese_summary       TEXT NULL,
                category              TEXT NULL,
                passed_ai_filter      BOOLEAN NOT NULL DEFAULT FALSE
            );

            -- 按采集日期建索引，加速 GetByDate 查询
            CREATE INDEX IF NOT EXISTS idx_articles_collected_at ON articles (collected_at);

            -- 日报表。ArticleIds 用 JSONB 存有序列表。
            CREATE TABLE IF NOT EXISTS daily_digests (
                date               TEXT PRIMARY KEY,
                article_ids        JSONB NOT NULL DEFAULT '[]',
                generated_at       TIMESTAMPTZ NOT NULL,
                total_collected    INTEGER NOT NULL DEFAULT 0,
                passed_coarse_count INTEGER NOT NULL DEFAULT 0,
                passed_ai_count    INTEGER NOT NULL DEFAULT 0
            );
            """;

        await using var cmd = new NpgsqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// Postgres 版文章仓储。每个方法独立开连接（短连接），适合 Serverless/免费平台。
/// 用参数化 SQL 防注入；ON CONFLICT DO NOTHING 保证幂等。
/// </summary>
public sealed class PostgresArticleRepository : IArticleRepository
{
    private readonly string _cs;
    public PostgresArticleRepository(string connectionString) => _cs = connectionString;

    public async Task<int> AddRangeAsync(IEnumerable<Article> articles, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        var added = 0;

        foreach (var a in articles)
        {
            // 用事务外单条 UPSERT：_id 重复则跳过，返回影响行数
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO articles
                    (id, source_id, original_title, url, summary, published_at, collected_at,
                     passed_coarse_filter, coarse_filter_reason, relevance_score,
                     chinese_title, chinese_summary, category, passed_ai_filter)
                VALUES
                    (@id, @source_id, @original_title, @url, @summary, @published_at, @collected_at,
                     @passed_coarse_filter, @coarse_filter_reason, @relevance_score,
                     @chinese_title, @chinese_summary, @category, @passed_ai_filter)
                ON CONFLICT (id) DO NOTHING
                """, conn);

            cmd.Parameters.AddWithValue("id", a.Id);
            cmd.Parameters.AddWithValue("source_id", a.SourceId);
            cmd.Parameters.AddWithValue("original_title", a.OriginalTitle);
            cmd.Parameters.AddWithValue("url", a.Url);
            cmd.Parameters.AddWithValue("summary", a.Summary ?? string.Empty);
            cmd.Parameters.AddWithValue("published_at", a.PublishedAt);
            cmd.Parameters.AddWithValue("collected_at", a.CollectedAt);
            cmd.Parameters.AddWithValue("passed_coarse_filter", a.PassedCoarseFilter);
            cmd.Parameters.AddWithValue("coarse_filter_reason", a.CoarseFilterReason ?? string.Empty);
            cmd.Parameters.AddWithValue("relevance_score", (object?)a.RelevanceScore ?? DBNull.Value);
            cmd.Parameters.AddWithValue("chinese_title", (object?)a.ChineseTitle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("chinese_summary", (object?)a.ChineseSummary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("category", (object?)a.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("passed_ai_filter", a.PassedAiFilter);

            added += await cmd.ExecuteNonQueryAsync(ct);
        }
        return added;
    }

    public async Task<IReadOnlyList<Article>> GetByDateAsync(string date, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM articles WHERE collected_at::date = @date::date ORDER BY collected_at DESC", conn);
        cmd.Parameters.AddWithValue("date", date);
        return await ReadArticlesAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<Article>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT * FROM articles ORDER BY collected_at DESC", conn);
        return await ReadArticlesAsync(cmd, ct);
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM articles WHERE id = @id)", conn);
        cmd.Parameters.AddWithValue("id", id);
        return (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
    }

    /// <summary>把 data reader 逐行映射成 Article 实体（集中处理 DBNull）。</summary>
    private static async Task<IReadOnlyList<Article>> ReadArticlesAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var list = new List<Article>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new Article
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                SourceId = reader.GetString(reader.GetOrdinal("source_id")),
                OriginalTitle = reader.GetString(reader.GetOrdinal("original_title")),
                Url = reader.GetString(reader.GetOrdinal("url")),
                Summary = reader.GetString(reader.GetOrdinal("summary")),
                PublishedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("published_at")),
                CollectedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("collected_at")),
                PassedCoarseFilter = reader.GetBoolean(reader.GetOrdinal("passed_coarse_filter")),
                CoarseFilterReason = reader.GetString(reader.GetOrdinal("coarse_filter_reason")),
                RelevanceScore = reader.IsDBNull(reader.GetOrdinal("relevance_score"))
                    ? null : reader.GetInt32(reader.GetOrdinal("relevance_score")),
                ChineseTitle = reader.IsDBNull(reader.GetOrdinal("chinese_title"))
                    ? null : reader.GetString(reader.GetOrdinal("chinese_title")),
                ChineseSummary = reader.IsDBNull(reader.GetOrdinal("chinese_summary"))
                    ? null : reader.GetString(reader.GetOrdinal("chinese_summary")),
                Category = reader.IsDBNull(reader.GetOrdinal("category"))
                    ? null : reader.GetString(reader.GetOrdinal("category")),
                PassedAiFilter = reader.GetBoolean(reader.GetOrdinal("passed_ai_filter"))
            });
        }
        return list;
    }
}

/// <summary>Postgres 版日报仓储。</summary>
public sealed class PostgresDigestRepository : IDailyDigestRepository
{
    private readonly string _cs;
    public PostgresDigestRepository(string connectionString) => _cs = connectionString;

    public async Task SaveAsync(DailyDigest digest, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO daily_digests
                (date, article_ids, generated_at, total_collected, passed_coarse_count, passed_ai_count)
            VALUES
                (@date, @article_ids::jsonb, @generated_at, @total, @coarse, @ai)
            ON CONFLICT (date) DO UPDATE SET
                article_ids = EXCLUDED.article_ids,
                generated_at = EXCLUDED.generated_at,
                total_collected = EXCLUDED.total_collected,
                passed_coarse_count = EXCLUDED.passed_coarse_count,
                passed_ai_count = EXCLUDED.passed_ai_count
            """, conn);

        cmd.Parameters.AddWithValue("date", digest.Date);
        cmd.Parameters.AddWithValue("article_ids", JsonSerializer.Serialize(digest.ArticleIds));
        cmd.Parameters.AddWithValue("generated_at", digest.GeneratedAt);
        cmd.Parameters.AddWithValue("total", digest.TotalCollected);
        cmd.Parameters.AddWithValue("coarse", digest.PassedCoarseCount);
        cmd.Parameters.AddWithValue("ai", digest.PassedAiCount);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<DailyDigest?> GetAsync(string date, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT * FROM daily_digests WHERE date = @date", conn);
        cmd.Parameters.AddWithValue("date", date);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<DailyDigest>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM daily_digests ORDER BY date DESC LIMIT @count", conn);
        cmd.Parameters.AddWithValue("count", count);
        var list = new List<DailyDigest>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Map(reader));
        return list;
    }

    private static DailyDigest Map(System.Data.Common.DbDataReader reader)
    {
        var idsJson = reader.GetString(reader.GetOrdinal("article_ids"));
        var ids = JsonSerializer.Deserialize<List<string>>(idsJson) ?? [];
        return new DailyDigest
        {
            Date = reader.GetString(reader.GetOrdinal("date")),
            ArticleIds = ids,
            GeneratedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("generated_at")),
            TotalCollected = reader.GetInt32(reader.GetOrdinal("total_collected")),
            PassedCoarseCount = reader.GetInt32(reader.GetOrdinal("passed_coarse_count")),
            PassedAiCount = reader.GetInt32(reader.GetOrdinal("passed_ai_count"))
        };
    }
}

/// <summary>Postgres 版来源仓储。源数据也在库里，首次启动由初始化器灌入种子。</summary>
public sealed class PostgresSourceRepository : ISourceRepository
{
    private readonly string _cs;
    public PostgresSourceRepository(string connectionString) => _cs = connectionString;

    public async Task<IReadOnlyList<Source>> GetAllEnabledAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT id, name, kind, url, weight, enabled FROM sources WHERE enabled = TRUE", conn);
        var list = new List<Source>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new Source
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Kind = reader.GetString(2),
                Url = reader.GetString(3),
                Weight = reader.GetInt32(4),
                Enabled = reader.GetBoolean(5)
            });
        }
        return list;
    }
}

/// <summary>
/// 种子源写入器：把内置的独立开发资讯源灌进 Postgres（仅在表为空时执行）。
/// 与 InMemorySourceRepository 的预置源保持一致。
/// </summary>
public sealed class PostgresSeeder
{
    private readonly string _cs;
    public PostgresSeeder(string connectionString) => _cs = connectionString;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);

        // 已有数据就不重复灌，避免覆盖用户自定义的源
        await using var check = new NpgsqlCommand("SELECT COUNT(*) FROM sources", conn);
        var count = Convert.ToInt32(await check.ExecuteScalarAsync(ct));
        if (count > 0) return;

        var seeds = new (string Id, string Name, string Kind, string Url, int Weight)[]
        {
            ("hackernews",  "Hacker News",   "hackernews", "https://news.ycombinator.com",        8),
            ("indiehackers","Indie Hackers", "rss",        "https://www.indiehackers.com/feed.xml",10),
            ("producthunt", "Product Hunt",  "rss",        "https://www.producthunt.com/feed",     7),
            ("sample",      "演示数据（本地）", "sample",   "local://sample",                       5)
        };

        foreach (var s in seeds)
        {
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO sources (id, name, kind, url, weight, enabled)
                VALUES (@id, @name, @kind, @url, @weight, TRUE)
                ON CONFLICT (id) DO NOTHING
                """, conn);
            cmd.Parameters.AddWithValue("id", s.Id);
            cmd.Parameters.AddWithValue("name", s.Name);
            cmd.Parameters.AddWithValue("kind", s.Kind);
            cmd.Parameters.AddWithValue("url", s.Url);
            cmd.Parameters.AddWithValue("weight", s.Weight);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
