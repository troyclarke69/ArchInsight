namespace ArchInsight.Domain;

public enum AssessmentType { Modernization, DataMaturity, ServiceConsistency }
public enum AssessmentStatus { Pending, Running, Completed, Failed }
public enum ArtifactType { ArchitectureDocument, TechnologyInventory, DatabaseInventory, OpenApiSpecification, ServiceManifest, SourceCodeSummary, DataPlatformInventory }
public enum FindingSeverity { Critical, High, Medium, Low }
public enum AIInsightStatus { Pending, Running, Completed, Failed, NotConfigured }
public enum AIQuestionStatus { Pending, Running, Completed, Failed, NotConfigured }
public enum AIInsightConfidence { Low, Medium, High }
public enum RoadmapActivityType { QuickWin, FoundationalInvestment, TransformationActivity }

public sealed class Assessment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public AssessmentType AssessmentType { get; set; }
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    public AssessmentStatus Status { get; set; } = AssessmentStatus.Pending;
    public string? ErrorMessage { get; set; }
    public List<AssessmentArtifact> Artifacts { get; set; } = [];
    public AssessmentResult? Result { get; set; }
    public List<AssessmentInsight> Insights { get; set; } = [];
    public List<AIQuestion> AIQuestions { get; set; } = [];
}

public sealed class AssessmentArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentId { get; set; }
    public ArtifactType ArtifactType { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string StoragePath { get; set; } = "";
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset UploadedDate { get; set; } = DateTimeOffset.UtcNow;
    public Assessment? Assessment { get; set; }
}

public sealed class AssessmentResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentId { get; set; }
    public int OverallScore { get; set; }
    public string Recommendation { get; set; } = "";
    public string ExecutiveSummary { get; set; } = "";
    public int ApplicationCount { get; set; }
    public DateTimeOffset CompletedDate { get; set; } = DateTimeOffset.UtcNow;
    public Assessment? Assessment { get; set; }
    public List<Finding> Findings { get; set; } = [];
    public List<Recommendation> Recommendations { get; set; } = [];
    public List<ScoreCard> ScoreCards { get; set; } = [];
}

public sealed class Finding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentResultId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public FindingSeverity Severity { get; set; }
    public string RecommendationText { get; set; } = "";
    public AssessmentResult? AssessmentResult { get; set; }
}

public sealed class Recommendation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentResultId { get; set; }
    public int Priority { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Timeframe { get; set; } = "";
    public AssessmentResult? AssessmentResult { get; set; }
}

public sealed class ScoreCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentResultId { get; set; }
    public string Dimension { get; set; } = "Overall";
    public int Score { get; set; }
    public AssessmentResult? AssessmentResult { get; set; }
}

public sealed class AssessmentInsight
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentId { get; set; }
    public AIInsightStatus Status { get; set; } = AIInsightStatus.Pending;
    public string? ExecutiveSummary { get; set; }
    public string? CurrentState { get; set; }
    public string KeyRisksJson { get; set; } = "[]";
    public string? BusinessImpact { get; set; }
    public string? OperationalRisk { get; set; }
    public string? TechnicalDebtImpact { get; set; }
    public string? CloudReadiness { get; set; }
    public string? DeliveryVelocityImpact { get; set; }
    public string? DataAndReportingImpact { get; set; }
    public string? RiskNarrative { get; set; }
    public string? RecommendedStrategy { get; set; }
    public string RecommendedNextStepsJson { get; set; } = "[]";
    public string? ExpectedOutcome { get; set; }
    public AIInsightConfidence? Confidence { get; set; }
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? GeneratedAt { get; set; }
    public string? ModelName { get; set; }
    public string? PromptVersion { get; set; }
    public int? TokenUsage { get; set; }
    public string? ErrorMessage { get; set; }
    public Assessment? Assessment { get; set; }
    public List<AIRecommendation> Recommendations { get; set; } = [];
    public List<AIInsightRoadmapItem> Roadmap { get; set; } = [];
}

public sealed class AIRecommendation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InsightId { get; set; }
    public int Priority { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string BusinessBenefit { get; set; } = "";
    public string EstimatedComplexity { get; set; } = "";
    public string ExpectedOutcome { get; set; } = "";
    public AssessmentInsight? Insight { get; set; }
}

public sealed class AIInsightRoadmapItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InsightId { get; set; }
    public int Sequence { get; set; }
    public string Period { get; set; } = "";
    public RoadmapActivityType ActivityType { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string ExpectedOutcome { get; set; } = "";
    public AssessmentInsight? Insight { get; set; }
}

public sealed class AIQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentId { get; set; }
    public string Question { get; set; } = "";
    public string? Answer { get; set; }
    public AIQuestionStatus Status { get; set; } = AIQuestionStatus.Pending;
    public DateTimeOffset AskedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ModelName { get; set; }
    public string? PromptVersion { get; set; }
    public int? TokenUsage { get; set; }
    public string? ErrorMessage { get; set; }
    public Assessment? Assessment { get; set; }
}
