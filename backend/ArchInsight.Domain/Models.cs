namespace ArchInsight.Domain;

public enum AssessmentType { Modernization, DataMaturity, ServiceConsistency }
public enum AssessmentStatus { Pending, Running, Completed, Failed }
public enum ArtifactType { ArchitectureDocument, TechnologyInventory, DatabaseInventory, OpenApiSpecification, ServiceManifest, SourceCodeSummary, DataPlatformInventory }
public enum FindingSeverity { Critical, High, Medium, Low }

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
