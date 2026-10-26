using ArchInsight.Domain;

namespace ArchInsight.Simulation;

public sealed record PortfolioOpportunity(Guid ApplicationId, string ApplicationName, string RepositoryName, int TechnicalDebt, int BusinessImpact, int DependencyComplexity, int SecurityRisk, int PriorityScore, string PriorityBand, int CurrentScore, int ProjectedScore, string EffortLevel, string Recommendation);
public sealed record DependencyImpact(string SelectedSystem, IReadOnlyList<string> DirectDependencies, IReadOnlyList<string> DownstreamSystems, IReadOnlyList<string> AffectedApplications, int BlastRadius, bool IsSinglePointOfFailure);

/// <summary>Deterministic planning calculations. Assessment entities are read-only inputs.</summary>
public sealed class TransformationSimulator
{
    public ScenarioResult Simulate(Assessment assessment, Scenario scenario)
    {
        if (assessment.Status != AssessmentStatus.Completed || assessment.Result is null)
            throw new InvalidOperationException("Scenarios require a completed assessment with a baseline result.");
        if (scenario.Changes.Count == 0) throw new InvalidOperationException("Add at least one change before running a simulation.");

        var baseline = assessment.Result.OverallScore;
        var cloud = assessment.Result.ScoreCards.FirstOrDefault(x => x.Dimension.Equals("Cloud Readiness", StringComparison.OrdinalIgnoreCase))?.Score ?? (assessment.RepositoryAnalysis is null ? 35 : 20);
        var impact = 3;
        var effort = 0;
        var uplift = 0;
        var cloudChange = 0;
        var applied = new List<string>();
        foreach (var change in scenario.Changes)
        {
            var apps = assessment.RepositoryAnalysis?.Applications ?? [];
            var targetApps = change.Target.Equals("Portfolio", StringComparison.OrdinalIgnoreCase)
                ? apps.ToArray()
                : apps.Where(x => x.Name.Equals(change.Target, StringComparison.OrdinalIgnoreCase)).ToArray();
            impact = Math.Max(impact, targetApps.Select(x => x.BusinessImpact).DefaultIfEmpty(3).Max());
            switch (change.ChangeType)
            {
                case ScenarioChangeType.RuntimeUpgrade:
                    uplift += targetApps.Any(x => x.Ecosystem.Equals(".NET", StringComparison.OrdinalIgnoreCase) && (x.Runtime.Contains("framework", StringComparison.OrdinalIgnoreCase) || x.Runtime.Contains("4.", StringComparison.OrdinalIgnoreCase))) ? 14 : 8;
                    effort += 3; applied.Add("modern runtime"); break;
                case ScenarioChangeType.DatabaseUpgrade:
                    uplift += targetApps.Any(x => x.Database.Contains("2012", StringComparison.OrdinalIgnoreCase)) || assessment.Result.Findings.Any(x => x.Category.Contains("Database", StringComparison.OrdinalIgnoreCase)) ? 12 : 7;
                    effort += 3; applied.Add("supported database"); break;
                case ScenarioChangeType.Containerize:
                    uplift += targetApps.Any(x => !x.Containerized) ? 7 : 2; cloudChange += 18; effort += 2; applied.Add("containerization"); break;
                case ScenarioChangeType.MoveToCloud:
                    uplift += 5; cloudChange += 24; effort += 4; applied.Add("cloud migration"); break;
                case ScenarioChangeType.StandardizeAuthentication:
                    uplift += assessment.RepositoryAnalysis?.Services.Any(x => string.IsNullOrWhiteSpace(x.AuthenticationJson) || x.AuthenticationJson == "[]") == true ? 8 : 4;
                    effort += 2; applied.Add("authentication standardization"); break;
                case ScenarioChangeType.StandardizeErrorHandling:
                    uplift += 5; effort += 2; applied.Add("error handling standardization"); break;
                case ScenarioChangeType.SplitMonolith:
                    uplift += 8; cloudChange += 8; effort += 5; applied.Add("service decomposition"); break;
                case ScenarioChangeType.RemoveLegacyDependency:
                    uplift += 9; effort += 2; applied.Add("legacy dependency removal"); break;
            }
        }
        uplift = Math.Min(100 - baseline, uplift);
        var projected = Math.Clamp(baseline + uplift, 0, 100);
        var riskReduction = Math.Max(0, projected - baseline);
        var effortPoints = Math.Clamp(effort * 10, 10, 100);
        var priority = Math.Clamp((int)Math.Round(riskReduction * 0.35 + impact * 20 * 0.30 + Math.Max(0, 100 - effortPoints) * 0.20 + Math.Min(100, assessment.Result.Findings.Count(f => f.Severity is FindingSeverity.High or FindingSeverity.Critical) * 20) * 0.15), 0, 100);
        var projectedCloud = Math.Clamp(cloud + cloudChange, 0, 100);
        return new ScenarioResult
        {
            BaselineScore = baseline, ProjectedScore = projected, RiskReduction = riskReduction,
            CloudReadinessChange = projectedCloud - cloud, TechnicalDebtReduction = riskReduction,
            ImplementationEffort = effortPoints, BusinessImpact = impact * 20, PriorityScore = priority,
            EffortLevel = effortPoints <= 35 ? "Low" : effortPoints <= 70 ? "Medium" : "High",
            RiskLevel = projected >= 75 ? "Low" : projected >= 55 ? "Medium" : "High",
            Recommendation = projected switch { <= 39 => "Rewrite", <= 59 => "Refactor", <= 79 => "Replatform", _ => "Lift-and-Shift" },
            Summary = $"Projected score improves from {baseline} to {projected} (+{riskReduction}). Cloud readiness changes by {projectedCloud - cloud} points. This deterministic estimate reflects {string.Join(", ", applied.Distinct())}; validate effort and dependencies with delivery teams."
        };
    }

    public IReadOnlyList<PortfolioOpportunity> Prioritize(RepositoryAnalysis repository)
    {
        var apps = repository.Applications;
        return apps.Select(app =>
        {
            var ownedDependencies = repository.Dependencies.Count(d => d.RepositoryPath.Equals(app.RepositoryPath, StringComparison.OrdinalIgnoreCase));
            var complexity = Math.Min(100, ownedDependencies * 8 + (app.Containerized ? 0 : 15));
            var security = Math.Min(100, repository.Dependencies.Count(d => d.RepositoryPath.Equals(app.RepositoryPath, StringComparison.OrdinalIgnoreCase) && (d.RiskLevel is "High" or "Critical")) * 35);
            var current = Math.Clamp(100 - app.TechnicalRisk, 0, 100);
            var projected = Math.Min(100, current + Math.Max(8, app.TechnicalRisk / 2));
            var score = Math.Clamp((int)Math.Round(app.TechnicalRisk * 0.35 + app.BusinessImpact * 20 * 0.30 + complexity * 0.20 + security * 0.15), 0, 100);
            return new PortfolioOpportunity(app.Id, app.Name, repository.Name, app.TechnicalRisk, app.BusinessImpact * 20, complexity, security, score, score >= 70 ? "Transform" : score >= 45 ? "Invest" : "Monitor", current, projected, complexity >= 70 ? "High" : complexity >= 35 ? "Medium" : "Low", score >= 70 ? "Prioritize modernization discovery" : score >= 45 ? "Plan targeted risk reduction" : "Monitor and maintain");
        }).OrderByDescending(x => x.PriorityScore).ToArray();
    }

    public DependencyImpact AnalyzeImpact(RepositoryAnalysis repository, string selected)
    {
        var names = repository.Applications.Select(a => a.Name).Concat(repository.Services.Select(s => s.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!names.Contains(selected, StringComparer.OrdinalIgnoreCase)) throw new KeyNotFoundException("The selected system was not found in this repository inventory.");
        var direct = repository.ApplicationDependencies.Where(d => d.Source.Equals(selected, StringComparison.OrdinalIgnoreCase)).Select(d => d.Target).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var downstream = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frontier = new Queue<string>(); frontier.Enqueue(selected);
        while (frontier.TryDequeue(out var current))
            foreach (var edge in repository.ApplicationDependencies.Where(d => d.Target.Equals(current, StringComparison.OrdinalIgnoreCase)))
                if (downstream.Add(edge.Source)) frontier.Enqueue(edge.Source);
        var affected = new[] { selected }.Concat(downstream).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var dependents = repository.ApplicationDependencies.Count(d => d.Target.Equals(selected, StringComparison.OrdinalIgnoreCase));
        return new DependencyImpact(selected, direct, downstream.ToArray(), affected, affected.Length, dependents >= 2 && affected.Length >= 3);
    }
}
