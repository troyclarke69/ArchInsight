using ArchInsight.Analysis;

using ArchInsight.Domain;

namespace ArchInsight.Agents.Contracts;

public sealed record AgentContext(RepositoryInventory Inventory, Assessment? Assessment = null, AssessmentInsight? Insight = null);
public sealed record AgentFinding(string Title, string Description, string Category, string Severity, string Recommendation, int ScorePenalty);
public sealed record GeneratedArtifact(string Type, string FileName, string MetadataJson);
public sealed record AgentResult(string AgentName, string Summary, IReadOnlyList<AgentFinding> Findings, IReadOnlyList<GeneratedArtifact> Artifacts, string OutputJson);

public interface IAgent
{
    string Name { get; }
    Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct);
}

public interface IAgentOrchestrator
{
    Task<IReadOnlyList<AgentResult>> ExecuteAsync(AgentContext context, CancellationToken ct);
}
