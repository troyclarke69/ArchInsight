using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArchInsight.Analysis;
using ArchInsight.Domain;

namespace ArchLens.Governance;

public sealed record PullRequestFinding(string Category, string Severity, string Title, string Description, string AffectedPath);
public sealed record PullRequestAssessment(int RiskScore, string RiskLevel, string Summary, IReadOnlyList<PullRequestFinding> Findings);

/// <summary>Local, repeatable architecture policy and drift rules. No model calls or hosted repository access.</summary>
public sealed class ArchitectureGovernanceEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public ArchitectureSnapshot CreateSnapshot(Guid repositoryId, RepositoryInventory inventory)
    {
        var frameworks = inventory.Applications.SelectMany(a => a.Frameworks.DefaultIfEmpty(a.Runtime)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        var technologies = inventory.Inventory.Select(x => new { x.Category, x.Name, x.Version }).Concat(inventory.Dependencies.Select(x => new { Category = x.Type, x.Name, x.Version })).Distinct().ToArray();
        var canonical = JsonSerializer.Serialize(new { inventory.Applications, inventory.Services, inventory.Dependencies, inventory.Inventory }, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var appRisk = inventory.Applications.Select(a => a.Ecosystem.Equals(".NET", StringComparison.OrdinalIgnoreCase) && IsLegacyFramework(a.Runtime) ? 70 : 25).ToArray();
        var depRisk = inventory.Dependencies.Select(d => IsLegacyTechnology(d.Name, d.Version) ? 80 : IsFloating(d.Version) ? 45 : 15).ToArray();
        var risk = Average(appRisk.Concat(depRisk), 35);
        var modern = 100 - risk;
        var cloud = Average(inventory.Applications.Select(a => (a.Containerized ? 45 : 0) + (IsModernRuntime(a.Runtime) ? 35 : 10) + (a.Hosting.Contains("cloud", StringComparison.OrdinalIgnoreCase) ? 20 : 0)), 20);
        var security = inventory.Services.Count == 0 ? 30 : (int)Math.Round(100d * inventory.Services.Count(s => s.AuthenticationSchemes.Count > 0) / inventory.Services.Count);
        security = Math.Clamp(security - inventory.Dependencies.Count(d => IsLegacyTechnology(d.Name, d.Version)) * 5, 0, 100);
        var consistency = inventory.Services.Count == 0 ? 25 : Math.Clamp(100 - (int)Math.Round(100d * inventory.Services.Count(s => s.AuthenticationSchemes.Count == 0) / inventory.Services.Count) - Math.Max(0, inventory.Services.Select(s => s.Version).Distinct(StringComparer.OrdinalIgnoreCase).Count() - 1) * 8, 0, 100);
        var maturity = (int)Math.Round((modern + cloud + security + consistency) / 4d);
        return new ArchitectureSnapshot
        {
            RepositoryAnalysisId = repositoryId, Hash = hash,
            ApplicationCount = inventory.Applications.Count, ServiceCount = inventory.Services.Count, DependencyCount = inventory.Dependencies.Count,
            FrameworkInventoryJson = JsonSerializer.Serialize(frameworks), TechnologyInventoryJson = JsonSerializer.Serialize(technologies),
            SummaryJson = JsonSerializer.Serialize(inventory), RiskScore = risk, ModernizationScore = modern, TechnicalDebt = risk,
            CloudReadiness = cloud, SecurityScore = security, ConsistencyScore = consistency, MaturityIndex = maturity
        };
    }

    public IReadOnlyList<ArchitecturePolicy> DefaultPolicies() =>
    [
        new() { Name = "No .NET Framework applications", Description = "New or monitored applications must use a supported .NET runtime.", Category = "Modernization", RuleType = "NoLegacyFramework", Severity = "High" },
        new() { Name = "SQL Server 2019 or newer", Description = "Avoid SQL Server releases older than 2019.", Category = "Technology", RuleType = "SqlServer2019OrNewer", Severity = "High" },
        new() { Name = "JWT or OAuth required for APIs", Description = "Every discovered API must declare JWT, bearer, or OAuth authentication.", Category = "Security", RuleType = "AuthenticationRequired", Severity = "Critical" },
        new() { Name = "Containerization required", Description = "Discovered applications should have container deployment evidence.", Category = "Cloud", RuleType = "ContainerizationRequired", Severity = "Medium" },
        new() { Name = "OpenAPI contract required", Description = "API services should have an OpenAPI contract in the repository.", Category = "Governance", RuleType = "OpenApiRequired", Severity = "Medium" },
        new() { Name = "Observability libraries required", Description = "Applications should include a recognized logging or telemetry dependency.", Category = "Operations", RuleType = "ObservabilityRequired", Severity = "Medium" }
    ];

    public IReadOnlyList<TechnologyStandard> DefaultStandards() =>
    [
        new() { Technology = ".NET", VersionPattern = "9.x", Lifecycle = "Approved", Rationale = "Preferred current enterprise runtime." },
        new() { Technology = ".NET", VersionPattern = "8.x", Lifecycle = "Approved", Rationale = "Supported long-term runtime baseline." },
        new() { Technology = ".NET Framework", VersionPattern = "4.x", Lifecycle = "Retire", Rationale = "Legacy runtime; migrate to modern .NET." },
        new() { Technology = "SQL Server", VersionPattern = "2022", Lifecycle = "Approved", Rationale = "Preferred database baseline." },
        new() { Technology = "SQL Server", VersionPattern = "2019", Lifecycle = "Approved", Rationale = "Minimum supported database baseline." },
        new() { Technology = "SQL Server", VersionPattern = "2016", Lifecycle = "Prohibited", Rationale = "Below the organization database baseline." },
        new() { Technology = "SQL Server", VersionPattern = "2012", Lifecycle = "Prohibited", Rationale = "End-of-life database baseline." },
        new() { Technology = "Node.js", VersionPattern = "22.x", Lifecycle = "Approved", Rationale = "Preferred supported Node.js baseline." },
        new() { Technology = "Node.js", VersionPattern = "18.x", Lifecycle = "Watch", Rationale = "Review runtime lifecycle and upgrade plans." }
    ];

    public IReadOnlyList<PolicyEvaluation> Evaluate(ArchitectureSnapshot snapshot, RepositoryInventory inventory, IReadOnlyList<ArchitecturePolicy> policies)
    {
        var evaluations = new List<PolicyEvaluation>();
        foreach (var policy in policies.Where(p => p.Enabled))
        {
            var failures = new List<(string Asset, string Evidence)>();
            switch (policy.RuleType)
            {
                case "NoLegacyFramework":
                    failures.AddRange(inventory.Applications.Where(a => IsLegacyFramework(a.Runtime) || a.Frameworks.Any(IsLegacyFramework)).Select(a => (a.Name, $"Runtime {a.Runtime} is a legacy .NET Framework target.")));
                    break;
                case "SqlServer2019OrNewer":
                    failures.AddRange(inventory.Applications.Where(a => IsOldSql(a.Database)).Select(a => (a.Name, $"Detected database technology: {a.Database}.")));
                    failures.AddRange(inventory.Dependencies.Where(d => IsOldSql(d.Name + " " + d.Version)).Select(d => (d.Name, $"Detected legacy database component {d.Name} {d.Version}.")));
                    break;
                case "AuthenticationRequired":
                    failures.AddRange(inventory.Services.Where(s => s.AuthenticationSchemes.Count == 0 || !s.AuthenticationSchemes.Any(IsAcceptedAuthentication)).Select(s => (s.Name, "No accepted JWT, bearer, or OAuth scheme was detected.")));
                    break;
                case "ContainerizationRequired":
                    failures.AddRange(inventory.Applications.Where(a => !a.Containerized).Select(a => (a.Name, "No container evidence was found for this application.")));
                    break;
                case "OpenApiRequired":
                    if (inventory.Applications.Count > 0 && inventory.Services.Count == 0) failures.Add(("Repository", "No OpenAPI document was discovered for applications in this repository."));
                    break;
                case "ObservabilityRequired":
                    var observable = inventory.Dependencies.Any(d => Regex.IsMatch(d.Name, "OpenTelemetry|ApplicationInsights|Serilog|NLog|Winston|Pino", RegexOptions.IgnoreCase));
                    if (inventory.Applications.Count > 0 && !observable) failures.Add(("Repository", "No recognized telemetry or logging dependency was discovered."));
                    break;
            }
            if (failures.Count == 0)
                evaluations.Add(new PolicyEvaluation { ArchitectureSnapshotId = snapshot.Id, ArchitecturePolicyId = policy.Id, Asset = "Repository", Status = GovernanceStatus.Pass, Evidence = "No violating evidence was detected by this static rule." });
            else
                evaluations.AddRange(failures.Select(f => new PolicyEvaluation { ArchitectureSnapshotId = snapshot.Id, ArchitecturePolicyId = policy.Id, Asset = f.Asset, Status = policy.Severity is "Critical" or "High" ? GovernanceStatus.Fail : GovernanceStatus.Warning, Evidence = f.Evidence }));
        }
        return evaluations;
    }

    public IReadOnlyList<DriftAlert> Compare(ArchitectureSnapshot previous, ArchitectureSnapshot current)
    {
        var before = DeserializeInventory(previous.SummaryJson);
        var after = DeserializeInventory(current.SummaryJson);
        var alerts = new List<DriftAlert>();
        void Add(string category, string severity, string title, string description, string asset) => alerts.Add(new DriftAlert { RepositoryAnalysisId = current.RepositoryAnalysisId, PreviousSnapshotId = previous.Id, CurrentSnapshotId = current.Id, Category = category, Severity = severity, Title = title, Description = description, AffectedAsset = asset });

        var oldDependencies = before.Dependencies.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var dep in after.Dependencies.Where(d => !oldDependencies.Contains(Key(d)) && IsLegacyTechnology(d.Name, d.Version)))
            Add("New technical debt", "High", "Legacy technology introduced", $"New dependency {dep.Name} {dep.Version} is classified as legacy by the local baseline.", dep.Path);
        var previousApps = before.Applications.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var app in after.Applications)
        {
            if (previousApps.TryGetValue(app.Name, out var old) && !IsLegacyFramework(old.Runtime) && IsLegacyFramework(app.Runtime))
                Add("Technology regression", "Critical", "Runtime regressed to .NET Framework", $"{app.Name} changed from {old.Runtime} to {app.Runtime}.", app.Name);
            else if (!previousApps.ContainsKey(app.Name) && IsLegacyFramework(app.Runtime))
                Add("New technical debt", "High", "Legacy application added", $"New application {app.Name} targets {app.Runtime}.", app.Path);
        }
        var previousServices = before.Services.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var service in after.Services)
        {
            if (previousServices.TryGetValue(service.Name, out var old) && old.AuthenticationSchemes.Count > 0 && service.AuthenticationSchemes.Count == 0)
                Add("Security regression", "Critical", "API authentication removed", $"{service.Name} previously declared authentication ({string.Join(", ", old.AuthenticationSchemes)}) but the new contract declares none.", service.Path);
            if (!previousServices.ContainsKey(service.Name) && !Regex.IsMatch(service.Name, "^[A-Z][A-Za-z0-9]*(Api|Service)$"))
                Add("Service inconsistency", "Medium", "New API name does not follow the naming convention", $"Service '{service.Name}' does not follow PascalCase with an Api or Service suffix.", service.Path);
        }
        foreach (var dep in after.Dependencies.Where(d => !oldDependencies.Contains(Key(d)) && (string.IsNullOrWhiteSpace(d.Version) || d.Version is "unknown" or "*" || d.Version.Contains('*'))))
            Add("Dependency drift", "Medium", "Unmanaged dependency introduced", $"New dependency {dep.Name} has no pinned version ({dep.Version}).", dep.Path);
        if (current.RiskScore >= previous.RiskScore + 10)
            Add("Risk regression", current.RiskScore >= previous.RiskScore + 25 ? "Critical" : "High", "Architecture risk increased", $"Risk score increased from {previous.RiskScore} to {current.RiskScore}.", "Repository");
        if (current.ModernizationScore + 10 <= previous.ModernizationScore)
            Add("Modernization regression", "High", "Modernization score declined", $"Modernization score changed from {previous.ModernizationScore} to {current.ModernizationScore}.", "Repository");
        return alerts;
    }

    public PullRequestAssessment AnalyzePullRequest(string diff, string title)
    {
        var findings = new List<PullRequestFinding>();
        foreach (var file in Regex.Matches(diff, "(?m)^\\+\\+\\+ b/(?<path>.+)$").Select(m => m.Groups["path"].Value).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (file.Contains(".csproj", StringComparison.OrdinalIgnoreCase) || file.EndsWith("package.json", StringComparison.OrdinalIgnoreCase) || file.EndsWith("requirements.txt", StringComparison.OrdinalIgnoreCase))
                findings.Add(new("Dependency change", "Medium", "Technology manifest changed", "Review new and removed package versions against technology standards.", file));
            if (file.Contains("openapi", StringComparison.OrdinalIgnoreCase) || file.Contains("swagger", StringComparison.OrdinalIgnoreCase))
                findings.Add(new("API contract", "Medium", "API contract changed", "Review endpoint compatibility, authentication, and schema consistency.", file));
            if (file.Contains("auth", StringComparison.OrdinalIgnoreCase) || file.Contains("security", StringComparison.OrdinalIgnoreCase))
                findings.Add(new("Security", "High", "Security-sensitive path changed", "Review authentication and authorization impact before merge.", file));
        }
        if (Regex.IsMatch(diff, @"(?m)^[-].*(TargetFramework>net[1-4]|Microsoft\.NET\.Framework|Authentication.*None|security: \[\])", RegexOptions.IgnoreCase))
            findings.Add(new("Regression", "High", "Potential architecture regression", "The diff removes or downgrades a runtime or security signal; compare with repository policies.", "Pull request diff"));
        if (findings.Count == 0) findings.Add(new("Review", "Low", "No high-risk architecture signal detected", "Static diff rules did not identify a specific regression. This is not a full source or security review.", "Pull request diff"));
        var score = Math.Clamp(findings.Sum(f => f.Severity switch { "Critical" => 40, "High" => 25, "Medium" => 12, _ => 4 }), 0, 100);
        var level = score >= 75 ? "Critical" : score >= 50 ? "High" : score >= 25 ? "Medium" : "Low";
        return new(score, level, $"'{title}' has {findings.Count} architecture review finding(s) and a static change-risk score of {score}/100 ({level}).", findings);
    }

    public static bool IsLegacyFramework(string value) => value.Contains(".NET Framework", StringComparison.OrdinalIgnoreCase) || value.Contains("net4", StringComparison.OrdinalIgnoreCase);
    public static bool IsLegacyTechnology(string name, string version) => IsOldSql(name + " " + version) || IsLegacyFramework(name + " " + version) || Regex.IsMatch(name, "(?i)(log4j|joda-time|system.web.mvc)") || version.Contains("*", StringComparison.Ordinal);
    private static bool IsOldSql(string value) => Regex.IsMatch(value, "(?i)SQL\\s*Server\\s*(2012|2014|2016|v11|v12|v13)");
    private static bool IsFloating(string version) => string.IsNullOrWhiteSpace(version) || version.Equals("unknown", StringComparison.OrdinalIgnoreCase) || version.Contains('*');
    private static bool IsModernRuntime(string runtime) => Regex.IsMatch(runtime, "(?i)(net[89]|netcoreapp[3-9]|node\\.?\\s*(20|22)|python\\s*3\\.(1[1-9]|[2-9][0-9]))");
    private static bool IsAcceptedAuthentication(string value) => Regex.IsMatch(value, "(?i)(jwt|bearer|oauth|oidc)");
    private static string Key(RepositoryDependency d) => $"{d.Ecosystem}|{d.Name}|{d.Version}";
    private static int Average(IEnumerable<int> values, int fallback) { var array = values.ToArray(); return array.Length == 0 ? fallback : Math.Clamp((int)Math.Round(array.Average()), 0, 100); }
    private static RepositoryInventory DeserializeInventory(string json) => JsonSerializer.Deserialize<RepositoryInventory>(json, JsonOptions) ?? new("Unknown", "", [], [], [], [], [], [], 0, []);
}
