namespace ArchInsight.Domain;

public enum AssessmentType { Modernization, DataMaturity, ServiceConsistency }
public enum AssessmentStatus { Pending, Running, Completed, Failed }
public enum ArtifactType { ArchitectureDocument, TechnologyInventory, DatabaseInventory, OpenApiSpecification, ServiceManifest, SourceCodeSummary, DataPlatformInventory, RepositoryArchive, SoftwareBillOfMaterials }
public enum FindingSeverity { Critical, High, Medium, Low }
public enum AIInsightStatus { Pending, Running, Completed, Failed, NotConfigured }
public enum AIQuestionStatus { Pending, Running, Completed, Failed, NotConfigured }
public enum AIInsightConfidence { Low, Medium, High }
public enum RoadmapActivityType { QuickWin, FoundationalInvestment, TransformationActivity }
public enum ScenarioStatus { Draft, Running, Completed, Failed }
public enum ScenarioChangeType { RuntimeUpgrade, DatabaseUpgrade, Containerize, MoveToCloud, StandardizeAuthentication, StandardizeErrorHandling, SplitMonolith, RemoveLegacyDependency }

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
    public Guid? RepositoryAnalysisId { get; set; }
    public RepositoryAnalysis? RepositoryAnalysis { get; set; }
    public List<AssessmentVersion> Versions { get; set; } = [];
    public List<Scenario> Scenarios { get; set; } = [];
}

public sealed class Scenario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = "Demo user";
    public ScenarioStatus Status { get; set; } = ScenarioStatus.Draft;
    public Assessment? Assessment { get; set; }
    public List<ScenarioChange> Changes { get; set; } = [];
    public ScenarioResult? Result { get; set; }
    public string? AdvisorExplanation { get; set; }
    public string? AdvisorModelName { get; set; }
    public string? AdvisorPromptVersion { get; set; }
    public int? AdvisorTokenUsage { get; set; }
}

public sealed class ScenarioChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScenarioId { get; set; }
    public ScenarioChangeType ChangeType { get; set; }
    public string Target { get; set; } = "Portfolio";
    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";
    public Scenario? Scenario { get; set; }
}

public sealed class ScenarioResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScenarioId { get; set; }
    public int BaselineScore { get; set; }
    public int ProjectedScore { get; set; }
    public int RiskReduction { get; set; }
    public int CloudReadinessChange { get; set; }
    public int TechnicalDebtReduction { get; set; }
    public int ImplementationEffort { get; set; }
    public int BusinessImpact { get; set; }
    public int PriorityScore { get; set; }
    public string EffortLevel { get; set; } = "Medium";
    public string RiskLevel { get; set; } = "Medium";
    public string Summary { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public DateTimeOffset SimulatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Scenario? Scenario { get; set; }
}

public sealed class ApplicationDependency
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepositoryAnalysisId { get; set; }
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public string DependencyType { get; set; } = "API";
    public RepositoryAnalysis? RepositoryAnalysis { get; set; }
}

public sealed class RepositoryAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ArchivePath { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    public string SummaryJson { get; set; } = "{}";
    public string SbomJson { get; set; } = "{}";
    public List<DiscoveredApplication> Applications { get; set; } = [];
    public List<DiscoveredService> Services { get; set; } = [];
    public List<TechnologyDependency> Dependencies { get; set; } = [];
    public List<SoftwareInventory> SoftwareInventory { get; set; } = [];
    public List<RepositoryAgentStage> AgentStages { get; set; } = [];
    public List<Assessment> Assessments { get; set; } = [];
    public List<ApplicationDependency> ApplicationDependencies { get; set; } = [];
}

public sealed class DiscoveredApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepositoryAnalysisId { get; set; }
    public string Name { get; set; } = "";
    public string RepositoryPath { get; set; } = "";
    public string Ecosystem { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string FrameworksJson { get; set; } = "[]";
    public string Database { get; set; } = "Unknown";
    public string Hosting { get; set; } = "Unknown";
    public bool Containerized { get; set; }
    public int BusinessImpact { get; set; } = 3;
    public int TechnicalRisk { get; set; }
    public RepositoryAnalysis? RepositoryAnalysis { get; set; }
}

public sealed class DiscoveredService
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepositoryAnalysisId { get; set; }
    public string Name { get; set; } = "";
    public string RepositoryPath { get; set; } = "";
    public string Version { get; set; } = "unversioned";
    public int EndpointCount { get; set; }
    public string EndpointsJson { get; set; } = "[]";
    public string SchemasJson { get; set; } = "[]";
    public string AuthenticationJson { get; set; } = "[]";
    public RepositoryAnalysis? RepositoryAnalysis { get; set; }
}

public sealed class TechnologyDependency
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepositoryAnalysisId { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "unknown";
    public string Type { get; set; } = "Library";
    public string Ecosystem { get; set; } = "";
    public string RepositoryPath { get; set; } = "";
    public string RiskLevel { get; set; } = "Unknown";
    public string RiskReason { get; set; } = "";
    public RepositoryAnalysis? RepositoryAnalysis { get; set; }
}

public sealed class SoftwareInventory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepositoryAnalysisId { get; set; }
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "unknown";
    public string SourcePath { get; set; } = "";
    public RepositoryAnalysis? RepositoryAnalysis { get; set; }
}

public sealed class RepositoryAgentStage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepositoryAnalysisId { get; set; }
    public int Sequence { get; set; }
    public string AgentName { get; set; } = "";
    public string Status { get; set; } = "Completed";
    public string OutputJson { get; set; } = "{}";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public RepositoryAnalysis? RepositoryAnalysis { get; set; }
}

public sealed class AssessmentVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentId { get; set; }
    public int Version { get; set; } = 1;
    public string RuleVersion { get; set; } = "phase2-v1";
    public string PromptVersion { get; set; } = "phase3-v1";
    public string AssessmentVersionLabel { get; set; } = "phase4-v1";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ResultSnapshotJson { get; set; } = "{}";
    public Assessment? Assessment { get; set; }
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
