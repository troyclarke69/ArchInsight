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
    public DbSet<RepositoryAnalysis> RepositoryAnalyses => Set<RepositoryAnalysis>();
    public DbSet<DiscoveredApplication> DiscoveredApplications => Set<DiscoveredApplication>();
    public DbSet<DiscoveredService> DiscoveredServices => Set<DiscoveredService>();
    public DbSet<TechnologyDependency> TechnologyDependencies => Set<TechnologyDependency>();
    public DbSet<SoftwareInventory> SoftwareInventories => Set<SoftwareInventory>();
    public DbSet<RepositoryAgentStage> RepositoryAgentStages => Set<RepositoryAgentStage>();
    public DbSet<AssessmentVersion> AssessmentVersions => Set<AssessmentVersion>();
    public DbSet<Scenario> Scenarios => Set<Scenario>();
    public DbSet<ScenarioChange> ScenarioChanges => Set<ScenarioChange>();
    public DbSet<ScenarioResult> ScenarioResults => Set<ScenarioResult>();
    public DbSet<ApplicationDependency> ApplicationDependencies => Set<ApplicationDependency>();
    public DbSet<ArchitectureSnapshot> ArchitectureSnapshots => Set<ArchitectureSnapshot>();
    public DbSet<DriftAlert> DriftAlerts => Set<DriftAlert>();
    public DbSet<ArchitecturePolicy> ArchitecturePolicies => Set<ArchitecturePolicy>();
    public DbSet<PolicyEvaluation> PolicyEvaluations => Set<PolicyEvaluation>();
    public DbSet<TechnologyStandard> TechnologyStandards => Set<TechnologyStandard>();
    public DbSet<ArchitectureDecision> ArchitectureDecisions => Set<ArchitectureDecision>();
    public DbSet<PullRequestReview> PullRequestReviews => Set<PullRequestReview>();
    public DbSet<TransformationInitiative> TransformationInitiatives => Set<TransformationInitiative>();
    public DbSet<EngineeringMetrics> EngineeringMetrics => Set<EngineeringMetrics>();
    public DbSet<KnowledgeNode> KnowledgeNodes => Set<KnowledgeNode>();
    public DbSet<KnowledgeRelationship> KnowledgeRelationships => Set<KnowledgeRelationship>();

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
        model.Entity<Assessment>().HasOne(a => a.RepositoryAnalysis).WithMany(r => r.Assessments).HasForeignKey(a => a.RepositoryAnalysisId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<Assessment>().HasMany(a => a.Versions).WithOne(v => v.Assessment).HasForeignKey(v => v.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Assessment>().HasMany(a => a.Scenarios).WithOne(s => s.Assessment).HasForeignKey(s => s.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Scenario>().Property(s => s.Status).HasConversion<string>();
        model.Entity<Scenario>().HasMany(s => s.Changes).WithOne(c => c.Scenario).HasForeignKey(c => c.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Scenario>().HasOne(s => s.Result).WithOne(r => r.Scenario).HasForeignKey<ScenarioResult>(r => r.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ScenarioChange>().Property(c => c.ChangeType).HasConversion<string>();
        model.Entity<ApplicationDependency>().HasIndex(d => new { d.RepositoryAnalysisId, d.Source, d.Target }).IsUnique();
        model.Entity<RepositoryAnalysis>().HasMany<ApplicationDependency>().WithOne(d => d.RepositoryAnalysis).HasForeignKey(d => d.RepositoryAnalysisId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ArchitectureSnapshot>().HasOne<RepositoryAnalysis>().WithMany().HasForeignKey(s => s.RepositoryAnalysisId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ArchitectureSnapshot>().HasIndex(s => new { s.RepositoryAnalysisId, s.CreatedDate });
        model.Entity<DriftAlert>().HasOne<RepositoryAnalysis>().WithMany().HasForeignKey(a => a.RepositoryAnalysisId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<DriftAlert>().HasIndex(a => new { a.RepositoryAnalysisId, a.Status, a.CreatedDate });
        model.Entity<PolicyEvaluation>().HasOne<ArchitectureSnapshot>().WithMany().HasForeignKey(e => e.ArchitectureSnapshotId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<PolicyEvaluation>().HasOne<ArchitecturePolicy>().WithMany().HasForeignKey(e => e.ArchitecturePolicyId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<PolicyEvaluation>().Property(e => e.Status).HasConversion<string>();
        model.Entity<PolicyEvaluation>().HasIndex(e => new { e.ArchitectureSnapshotId, e.ArchitecturePolicyId, e.Asset }).IsUnique();
        model.Entity<ArchitecturePolicy>().HasIndex(p => p.Name).IsUnique();
        model.Entity<TechnologyStandard>().HasIndex(s => new { s.Technology, s.VersionPattern }).IsUnique();
        model.Entity<ArchitectureDecision>().HasOne<RepositoryAnalysis>().WithMany().HasForeignKey(d => d.RepositoryAnalysisId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<PullRequestReview>().HasOne<RepositoryAnalysis>().WithMany().HasForeignKey(r => r.RepositoryAnalysisId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<PullRequestReview>().HasIndex(r => new { r.RepositoryAnalysisId, r.PullRequestNumber, r.HeadRevision });
        model.Entity<TransformationInitiative>().Property(i => i.Status).HasConversion<string>();
        model.Entity<TransformationInitiative>().HasIndex(i => new { i.Status, i.TargetCompletionDate });
        model.Entity<TransformationInitiative>().HasOne<Assessment>().WithMany().HasForeignKey(i => i.AssessmentId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<TransformationInitiative>().HasOne<Scenario>().WithMany().HasForeignKey(i => i.ScenarioId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<TransformationInitiative>().HasOne<Recommendation>().WithMany().HasForeignKey(i => i.RecommendationId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<TransformationInitiative>().HasOne<RepositoryAnalysis>().WithMany().HasForeignKey(i => i.RepositoryAnalysisId).OnDelete(DeleteBehavior.SetNull);
        model.Entity<EngineeringMetrics>().HasIndex(m => new { m.RepositoryAnalysisId, m.RecordedAt });
        model.Entity<KnowledgeNode>().HasIndex(n => new { n.RepositoryAnalysisId, n.NodeType, n.ExternalId }).IsUnique();
        model.Entity<KnowledgeRelationship>().HasOne<KnowledgeNode>().WithMany().HasForeignKey(r => r.FromNodeId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<KnowledgeRelationship>().HasOne<KnowledgeNode>().WithMany().HasForeignKey(r => r.ToNodeId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<KnowledgeRelationship>().HasIndex(r => new { r.FromNodeId, r.ToNodeId, r.RelationshipType }).IsUnique();
        model.Entity<RepositoryAnalysis>().HasMany(r => r.Applications).WithOne(a => a.RepositoryAnalysis).HasForeignKey(a => a.RepositoryAnalysisId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<RepositoryAnalysis>().HasMany(r => r.Services).WithOne(s => s.RepositoryAnalysis).HasForeignKey(s => s.RepositoryAnalysisId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<RepositoryAnalysis>().HasMany(r => r.Dependencies).WithOne(d => d.RepositoryAnalysis).HasForeignKey(d => d.RepositoryAnalysisId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<RepositoryAnalysis>().HasMany(r => r.SoftwareInventory).WithOne(s => s.RepositoryAnalysis).HasForeignKey(s => s.RepositoryAnalysisId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<RepositoryAnalysis>().HasMany(r => r.AgentStages).WithOne(s => s.RepositoryAnalysis).HasForeignKey(s => s.RepositoryAnalysisId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<AssessmentVersion>().HasIndex(v => new { v.AssessmentId, v.Version }).IsUnique();
        model.Entity<TechnologyDependency>().HasIndex(d => new { d.RepositoryAnalysisId, d.Name, d.Version });
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
        .Include(a => a.AIQuestions)
        .Include(a => a.Versions)
        .Include(a => a.RepositoryAnalysis)!.ThenInclude(r => r!.Applications)
        .Include(a => a.RepositoryAnalysis)!.ThenInclude(r => r!.Services)
        .Include(a => a.RepositoryAnalysis)!.ThenInclude(r => r!.Dependencies)
        .Include(a => a.RepositoryAnalysis)!.ThenInclude(r => r!.SoftwareInventory)
        .Include(a => a.RepositoryAnalysis)!.ThenInclude(r => r!.AgentStages)
        .AsSplitQuery();
    public Task<Assessment?> FindAsync(Guid id, CancellationToken ct) => Full(db.Assessments).SingleOrDefaultAsync(a => a.Id == id, ct);
    public async Task<IReadOnlyList<Assessment>> ListAsync(CancellationToken ct)
    {
        // EF Core's SQLite provider cannot translate DateTimeOffset ordering. Keep
        // date ordering in memory after the demo's assessment set has been loaded.
        var assessments = await Full(db.Assessments).ToListAsync(ct);
        return assessments.OrderByDescending(a => a.CreatedDate).ToArray();
    }
    public Task AddAsync(Assessment item, CancellationToken ct) => db.Assessments.AddAsync(item, ct).AsTask();
    public void AddVersion(AssessmentVersion version) => db.AssessmentVersions.Add(version);
    public void AddInsight(AssessmentInsight insight) => db.AssessmentInsights.Add(insight);
    public void AddQuestion(AIQuestion question) => db.AIQuestions.Add(question);
    public void AddArtifact(AssessmentArtifact artifact) => db.AssessmentArtifacts.Add(artifact);
    public void AddResult(AssessmentResult result) => db.AssessmentResults.Add(result);
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class JsonArtifactStorage(string contentRootPath) : IArtifactStorage
{
    public async Task<string> StoreAsync(Guid assessmentId, string fileName, Stream content, CancellationToken ct)
    {
        var directory = Path.Combine(contentRootPath, "uploads", assessmentId.ToString("N"));
        Directory.CreateDirectory(directory);
        var extension = Path.GetExtension(Path.GetFileName(fileName));
        if (extension.Length is 0 or > 10 || extension.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.')) extension = ".bin";
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(output, ct);
        return Path.GetRelativePath(contentRootPath, path);
    }
}
