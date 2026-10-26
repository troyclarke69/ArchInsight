using System.Text.Json;
using System.Text.RegularExpressions;
using ArchInsight.Agents.Contracts;
using ArchInsight.Analysis;

namespace ArchInsight.Agents.Services;

public sealed class ArtifactDiscoveryAgent : IAgent
{
    public string Name => "ArtifactDiscoveryAgent";
    public Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var i = context.Inventory;
        var output = new { applications = i.Applications, services = i.Services.Select(s => new { s.Name, s.Path, s.Version, s.EndpointCount, s.Endpoints, s.Schemas, s.AuthenticationSchemes }), dependencies = i.Dependencies };
        return Task.FromResult(Result(Name, $"Normalized {i.Applications.Count} applications, {i.Services.Count} API definitions, and {i.Dependencies.Count} dependencies.", output));
    }
    internal static AgentResult Result(string name, string summary, object output, IReadOnlyList<AgentFinding>? findings = null, IReadOnlyList<GeneratedArtifact>? artifacts = null) => new(name, summary, findings ?? [], artifacts ?? [], JsonSerializer.Serialize(output));
}

public sealed class ModernizationAgent : IAgent
{
    public string Name => "ModernizationAgent";
    public Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var apps = context.Inventory.Applications.Select(a => new { name = a.Name, framework = string.Join(",", a.Frameworks), database = a.Database, hosting = a.Hosting, containerized = a.Containerized, architecture = "application" });
        var artifact = new GeneratedArtifact("TechnologyInventory", "discovered-technology-inventory.json", JsonSerializer.Serialize(new { applications = apps }));
        return Task.FromResult(ArtifactDiscoveryAgent.Result(Name, $"Generated modernization input for {context.Inventory.Applications.Count} applications.", new { applicationCount = context.Inventory.Applications.Count, artifact = artifact.FileName }, artifacts: [artifact]));
    }
}

public sealed class ServiceGovernanceAgent : IAgent
{
    public string Name => "ServiceGovernanceAgent";
    public Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var services = context.Inventory.Services.Select(s => JsonSerializer.Deserialize<JsonElement>(s.ServiceManifestJson));
        var artifact = new GeneratedArtifact("ServiceManifest", "discovered-service-manifest.json", JsonSerializer.Serialize(new { services }));
        var findings = context.Inventory.Services.Where(s => s.AuthenticationSchemes.Count == 0).Select(s => new AgentFinding($"API authentication not declared: {s.Name}", $"No OpenAPI security scheme was found in {s.Path}.", "Authentication", "Medium", "Document the API security scheme and verify enforcement in implementation.", 8)).ToArray();
        return Task.FromResult(ArtifactDiscoveryAgent.Result(Name, $"Imported {context.Inventory.Services.Count} OpenAPI documents with {context.Inventory.Services.Sum(s => s.EndpointCount)} endpoints.", new { serviceCount = context.Inventory.Services.Count, endpointCount = context.Inventory.Services.Sum(s => s.EndpointCount), artifact = artifact.FileName }, findings, [artifact]));
    }
}

public sealed class TechnologyRiskAgent : IAgent
{
    private static readonly Dictionary<string, int> NugetMajorBaselines = new(StringComparer.OrdinalIgnoreCase) { ["Microsoft.EntityFrameworkCore"] = 9, ["Microsoft.AspNetCore.Mvc"] = 9, ["Microsoft.AspNetCore.Authentication.JwtBearer"] = 9 };
    public string Name => "TechnologyRiskAgent";
    public Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var findings = new List<AgentFinding>();
        foreach (var app in context.Inventory.Applications.Where(a => a.Ecosystem == ".NET"))
        {
            foreach (var framework in app.Frameworks)
            {
                if (framework.StartsWith("net4", StringComparison.OrdinalIgnoreCase)) findings.Add(new AgentFinding($"Legacy .NET Framework: {app.Name}", $"The project targets {framework} at {app.Path}.", "Framework age", "High", "Plan migration to a currently supported .NET runtime.", 20));
                else if (Regex.Match(framework, @"^net(?<major>\d+)(?:\.\d+)?$", RegexOptions.IgnoreCase) is { Success: true } match && int.TryParse(match.Groups["major"].Value, out var major) && major < 9) findings.Add(new AgentFinding($"Older .NET runtime: {app.Name}", $"The project targets {framework}; the configured discovery policy baseline is .NET 9.", "Framework age", "High", "Upgrade to a supported .NET runtime and validate package compatibility.", 15));
            }
        }
        foreach (var dependency in context.Inventory.Dependencies.Where(d => d.Ecosystem == "NuGet" && NugetMajorBaselines.ContainsKey(d.Name)))
        {
            var version = Regex.Match(dependency.Version, @"(?<major>\d+)");
            if (version.Success && int.TryParse(version.Groups["major"].Value, out var major) && major < NugetMajorBaselines[dependency.Name]) findings.Add(new AgentFinding($"Major version lag: {dependency.Name}", $"{dependency.Name} {dependency.Version} is below the configured discovery baseline {NugetMajorBaselines[dependency.Name]}.", "Dependency risk", "Medium", "Review release notes, compatibility, and upgrade options for this dependency.", 8));
        }
        foreach (var database in context.Inventory.Inventory.Where(i => i.Category == "Database" && i.Name.Contains("2012", StringComparison.OrdinalIgnoreCase))) findings.Add(new AgentFinding("SQL Server 2012 detected", $"A SQL Server 2012 reference was found at {database.Path}.", "Database risk", "Critical", "Upgrade the database engine and validate application compatibility.", 25));
        foreach (var dependency in context.Inventory.Dependencies.Where(d => d.Version is "*" or "latest" || d.Version.Contains("*", StringComparison.Ordinal))) findings.Add(new AgentFinding($"Unpinned dependency: {dependency.Name}", $"The dependency version '{dependency.Version}' is not a stable exact pin.", "Dependency risk", "Low", "Use lockfiles and review a reproducible dependency pinning strategy.", 4));
        return Task.FromResult(ArtifactDiscoveryAgent.Result(Name, $"Evaluated {context.Inventory.Dependencies.Count} dependency records against the configured discovery risk rules; {findings.Count} risks were found.", new { dependencyCount = context.Inventory.Dependencies.Count, baselinePolicy = "phase4-v1 (runtime and selected package major baselines)", findings }, findings));
    }
}

public sealed class ExecutiveAdvisorAgent : IAgent
{
    public string Name => "ExecutiveAdvisorAgent";
    public Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var i = context.Inventory;
        var assessment = context.Assessment;
        var insight = context.Insight;
        var output = new { repository = i.Name, applications = i.Applications.Count, apis = i.Services.Count, endpoints = i.Services.Sum(s => s.EndpointCount), dependencies = i.Dependencies.Count, databaseTechnologies = i.Inventory.Where(x => x.Category == "Database").Select(x => x.Name).Distinct(), buildWorkflows = i.BuildWorkflows.Count, deploymentWorkflows = i.DeploymentWorkflows.Count, deterministicScore = assessment?.Result?.OverallScore, deterministicRecommendation = assessment?.Result?.Recommendation, deterministicFindings = assessment?.Result?.Findings.Select(f => new { f.Title, severity = f.Severity.ToString() }), aiExecutiveSummary = insight?.ExecutiveSummary, aiRecommendations = insight?.Recommendations.OrderBy(r => r.Priority).Select(r => new { r.Priority, r.Title, r.BusinessBenefit }), roadmap = insight?.Roadmap.OrderBy(r => r.Sequence).Select(r => new { r.Period, r.Title, r.ExpectedOutcome }), importantCaveat = "Discovery is based on repository files and does not prove runtime behavior." };
        return Task.FromResult(ArtifactDiscoveryAgent.Result(Name, "Prepared an executive briefing package from discovery, deterministic findings, AI insight, and roadmap.", output));
    }
}
