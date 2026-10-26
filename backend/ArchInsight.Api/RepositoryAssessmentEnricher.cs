using System.Text.Json;
using System.Text.RegularExpressions;
using ArchInsight.Agents.Contracts;
using ArchInsight.Analysis;
using ArchInsight.Domain;

namespace ArchInsight.Api;

public static class RepositoryAssessmentEnricher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public static void ApplyDiscoveryFindings(Assessment assessment)
    {
        if (assessment.Result is null || assessment.RepositoryAnalysis is null) return;
        IEnumerable<RepositoryAgentStage> relevantStages = assessment.AssessmentType == AssessmentType.Modernization
            ? assessment.RepositoryAnalysis.AgentStages.Where(s => s.AgentName == "TechnologyRiskAgent")
            : assessment.AssessmentType == AssessmentType.ServiceConsistency
                ? assessment.RepositoryAnalysis.AgentStages.Where(s => s.AgentName == "ServiceGovernanceAgent")
                : [];
        foreach (var agentStage in relevantStages)
        {
            try
            {
                using var doc = JsonDocument.Parse(agentStage.OutputJson);
                if (!doc.RootElement.TryGetProperty("findings", out var findings) || findings.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in findings.Deserialize<AgentFinding[]>(JsonOptions) ?? [])
                {
                    if (assessment.Result.Findings.Any(f =>
                        (f.Category.Equals(item.Category, StringComparison.OrdinalIgnoreCase) && f.Title.Equals(item.Title, StringComparison.OrdinalIgnoreCase)) ||
                        (item.Category == "Framework age" && f.Category == "Framework age") ||
                        (item.Category == "Database risk" && f.Category == "Database"))) continue;
                    if (!Enum.TryParse<FindingSeverity>(item.Severity, true, out var severity)) severity = FindingSeverity.Low;
                    assessment.Result.Findings.Add(new Finding { AssessmentResultId = assessment.Result.Id, Title = item.Title, Description = item.Description, Category = item.Category, Severity = severity, RecommendationText = item.Recommendation });
                    assessment.Result.OverallScore = Math.Max(0, assessment.Result.OverallScore - Math.Clamp(item.ScorePenalty, 0, 100));
                    var nextPriority = assessment.Result.Recommendations.Count == 0 ? 1 : assessment.Result.Recommendations.Max(r => r.Priority) + 1;
                    assessment.Result.Recommendations.Add(new Recommendation { AssessmentResultId = assessment.Result.Id, Priority = nextPriority, Title = item.Title, Description = item.Recommendation, Timeframe = severity is FindingSeverity.Critical or FindingSeverity.High ? "0–3 months" : "3–6 months" });
                }
            }
            catch (JsonException) { }
        }
        if (assessment.AssessmentType == AssessmentType.Modernization)
            assessment.Result.Recommendation = assessment.Result.OverallScore switch { <= 39 => "Rewrite", <= 59 => "Refactor", <= 79 => "Replatform", _ => "Lift-and-Shift" };
        var overallCard = assessment.Result.ScoreCards.FirstOrDefault(s => s.Dimension.Equals("Overall", StringComparison.OrdinalIgnoreCase));
        if (overallCard is not null) overallCard.Score = assessment.Result.OverallScore;
        AddRepositoryMaturityDimensions(assessment);
        assessment.Result.ExecutiveSummary += " Repository discovery also evaluated dependency and service-governance rules; review the findings for repository evidence and limits.";
    }

    private static void AddRepositoryMaturityDimensions(Assessment assessment)
    {
        var repo = assessment.RepositoryAnalysis!;
        var inventory = JsonSerializer.Deserialize<RepositoryInventory>(repo.SummaryJson, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
        if (inventory is null) return;
        var appCount = Math.Max(1, inventory.Applications.Count);
        var authCoverage = inventory.Services.Count == 0 ? 20 : (int)Math.Round(20 + 80.0 * inventory.Services.Count(s => s.AuthenticationSchemes.Count > 0) / inventory.Services.Count);
        var containerCoverage = (int)Math.Round(100.0 * inventory.Applications.Count(a => a.Containerized) / appCount);
        var buildScore = (inventory.BuildWorkflows.Count > 0 ? 35 : 0) + (inventory.DeploymentWorkflows.Count > 0 ? 30 : 0) + (containerCoverage * 35 / 100);
        var modernRuntimeCoverage = inventory.Applications.Count == 0 ? 0 : (int)Math.Round(100.0 * inventory.Applications.Count(a => a.Frameworks.Any(f => f.StartsWith("net8", StringComparison.OrdinalIgnoreCase) || f.StartsWith("net9", StringComparison.OrdinalIgnoreCase) || f.StartsWith("Node.js", StringComparison.OrdinalIgnoreCase))) / appCount);
        var cloudScore = Math.Clamp((containerCoverage * 60 / 100) + (modernRuntimeCoverage * 40 / 100), 0, 100);
        var observable = inventory.Dependencies.Any(d => Regex.IsMatch(d.Name, "OpenTelemetry|ApplicationInsights|Serilog|NLog|Winston", RegexOptions.IgnoreCase));
        var observabilityScore = observable ? 75 : 20;
        var dimensions = new Dictionary<string, int>
        {
            ["Security Maturity"] = authCoverage,
            ["DevOps Maturity"] = buildScore,
            ["Cloud Readiness"] = cloudScore,
            ["Observability Maturity"] = observabilityScore
        };
        foreach (var dimension in dimensions)
            if (!assessment.Result!.ScoreCards.Any(s => s.Dimension.Equals(dimension.Key, StringComparison.OrdinalIgnoreCase)))
                assessment.Result.ScoreCards.Add(new ScoreCard { AssessmentResultId = assessment.Result.Id, Dimension = dimension.Key, Score = dimension.Value });
    }

    public static AssessmentVersion CreateVersion(Assessment assessment, int version)
    {
        var snapshot = assessment.Result is null ? "{}" : JsonSerializer.Serialize(new
        {
            assessment.Result.OverallScore,
            assessment.Result.Recommendation,
            scoreCards = assessment.Result.ScoreCards.Select(s => new { s.Dimension, s.Score }),
            findings = assessment.Result.Findings.Select(f => new { f.Title, f.Description, f.Category, severity = f.Severity.ToString() }),
            recommendations = assessment.Result.Recommendations.OrderBy(r => r.Priority).Select(r => new { r.Priority, r.Title, r.Description, r.Timeframe })
        });
        return new AssessmentVersion { AssessmentId = assessment.Id, Version = version, RuleVersion = assessment.RepositoryAnalysisId is null ? "phase2-v1" : "phase4-v1", PromptVersion = "phase3-v1", AssessmentVersionLabel = "phase4-v1", ResultSnapshotJson = snapshot };
    }
}
