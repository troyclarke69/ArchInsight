using ArchInsight.Domain;
using ArchInsight.Application;
using Microsoft.EntityFrameworkCore;

namespace ArchInsight.Infrastructure;

public sealed class AssessmentDbContext(DbContextOptions<AssessmentDbContext> options) : DbContext(options)
{
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<AssessmentArtifact> AssessmentArtifacts => Set<AssessmentArtifact>();
    public DbSet<AssessmentResult> AssessmentResults => Set<AssessmentResult>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<ScoreCard> ScoreCards => Set<ScoreCard>();
    public DbSet<AssessmentInsight> AssessmentInsights => Set<AssessmentInsight>();
    public DbSet<AIRecommendation> AIRecommendations => Set<AIRecommendation>();
    public DbSet<AIInsightRoadmapItem> AIInsightRoadmapItems => Set<AIInsightRoadmapItem>();
    public DbSet<AIQuestion> AIQuestions => Set<AIQuestion>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Assessment>().HasIndex(a => a.CreatedDate);
        model.Entity<Assessment>().Property(a => a.Name).HasMaxLength(120).IsRequired();
        model.Entity<Assessment>().Property(a => a.OrganizationName).HasMaxLength(120).IsRequired();
        model.Entity<Assessment>().Property(a => a.AssessmentType).HasConversion<string>();
        model.Entity<Assessment>().Property(a => a.Status).HasConversion<string>();
        model.Entity<Assessment>().HasMany(a => a.Artifacts).WithOne(x => x.Assessment).HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Assessment>().HasOne(a => a.Result).WithOne(r => r.Assessment).HasForeignKey<AssessmentResult>(r => r.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Assessment>().HasMany(a => a.Insights).WithOne(i => i.Assessment).HasForeignKey(i => i.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Assessment>().HasMany(a => a.AIQuestions).WithOne(q => q.Assessment).HasForeignKey(q => q.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<AssessmentInsight>().Property(i => i.Status).HasConversion<string>();
        model.Entity<AssessmentInsight>().Property(i => i.Confidence).HasConversion<string>();
        model.Entity<AssessmentInsight>().HasMany(i => i.Recommendations).WithOne(r => r.Insight).HasForeignKey(r => r.InsightId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<AssessmentInsight>().HasMany(i => i.Roadmap).WithOne(r => r.Insight).HasForeignKey(r => r.InsightId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<AIInsightRoadmapItem>().Property(i => i.ActivityType).HasConversion<string>();
        model.Entity<AIQuestion>().Property(q => q.Status).HasConversion<string>();
        model.Entity<AssessmentInsight>().HasIndex(i => new { i.AssessmentId, i.CreatedDate });
        model.Entity<AIQuestion>().HasIndex(q => new { q.AssessmentId, q.AskedAt });
        model.Entity<AssessmentResult>().HasMany(r => r.Findings).WithOne(f => f.AssessmentResult).HasForeignKey(f => f.AssessmentResultId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<AssessmentResult>().HasMany(r => r.Recommendations).WithOne(x => x.AssessmentResult).HasForeignKey(x => x.AssessmentResultId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<AssessmentResult>().HasMany(r => r.ScoreCards).WithOne(x => x.AssessmentResult).HasForeignKey(x => x.AssessmentResultId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<AssessmentArtifact>().Property(a => a.ArtifactType).HasConversion<string>();
        model.Entity<Finding>().Property(a => a.Severity).HasConversion<string>();
    }
}

public sealed class EfAssessmentRepository(AssessmentDbContext db) : IAssessmentRepository
{
    private static IQueryable<Assessment> Full(IQueryable<Assessment> query) => query
        .Include(a => a.Artifacts)
        .Include(a => a.Result)!.ThenInclude(r => r!.Findings)
        .Include(a => a.Result)!.ThenInclude(r => r!.Recommendations)
        .Include(a => a.Result)!.ThenInclude(r => r!.ScoreCards)
        .Include(a => a.Insights)!.ThenInclude(i => i!.Recommendations)
        .Include(a => a.Insights)!.ThenInclude(i => i!.Roadmap)
        .Include(a => a.AIQuestions);
    public Task<Assessment?> FindAsync(Guid id, CancellationToken ct) => Full(db.Assessments).SingleOrDefaultAsync(a => a.Id == id, ct);
    public async Task<IReadOnlyList<Assessment>> ListAsync(CancellationToken ct)
    {
        // EF Core's SQLite provider cannot translate DateTimeOffset ordering. Keep
        // date ordering in memory after the demo's assessment set has been loaded.
        var assessments = await Full(db.Assessments).ToListAsync(ct);
        return assessments.OrderByDescending(a => a.CreatedDate).ToArray();
    }
    public Task AddAsync(Assessment item, CancellationToken ct) => db.Assessments.AddAsync(item, ct).AsTask();
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class JsonArtifactStorage(string contentRootPath) : IArtifactStorage
{
    public async Task<string> StoreAsync(Guid assessmentId, string fileName, Stream content, CancellationToken ct)
    {
        var directory = Path.Combine(contentRootPath, "uploads", assessmentId.ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.json");
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(output, ct);
        return Path.GetRelativePath(contentRootPath, path);
    }
}
