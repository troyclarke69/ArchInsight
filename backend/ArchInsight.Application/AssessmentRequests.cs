using FluentValidation;
using MediatR;
using ArchInsight.Domain;

namespace ArchInsight.Application;

public sealed record CreateAssessmentCommand(string Name, string OrganizationName, AssessmentType AssessmentType) : IRequest<AssessmentSummary>;
public sealed class CreateAssessmentValidator : AbstractValidator<CreateAssessmentCommand>
{
    public CreateAssessmentValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.OrganizationName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.AssessmentType).IsInEnum();
    }
}
public sealed class CreateAssessmentHandler(IAssessmentRepository repository, IValidator<CreateAssessmentCommand> validator) : IRequestHandler<CreateAssessmentCommand, AssessmentSummary>
{
    public async Task<AssessmentSummary> Handle(CreateAssessmentCommand request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        var item = new Assessment { Name = request.Name.Trim(), OrganizationName = request.OrganizationName.Trim(), AssessmentType = request.AssessmentType };
        await repository.AddAsync(item, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return AssessmentMapping.Summary(item);
    }
}

public sealed record QueueAssessmentCommand(Guid Id) : IRequest<bool>;
public sealed class QueueAssessmentHandler(IAssessmentRepository repository, IAssessmentQueue queue) : IRequestHandler<QueueAssessmentCommand, bool>
{
    public async Task<bool> Handle(QueueAssessmentCommand request, CancellationToken cancellationToken)
    {
        var assessment = await repository.FindAsync(request.Id, cancellationToken);
        if (assessment is null || assessment.Status is AssessmentStatus.Running or AssessmentStatus.Completed) return false;
        if (assessment.Artifacts.Count == 0) throw new InvalidOperationException("Upload at least one JSON artifact before running this assessment.");
        assessment.Status = AssessmentStatus.Running;
        assessment.ErrorMessage = null;
        await repository.SaveChangesAsync(cancellationToken);
        await queue.EnqueueAsync(request.Id, cancellationToken);
        return true;
    }
}

public static class AssessmentMapping
{
    public static AssessmentSummary Summary(Assessment a) => new(a.Id, a.Name, a.OrganizationName, a.AssessmentType, a.Status, a.CreatedDate, a.Result?.OverallScore, a.Result?.Recommendation, a.Result?.Findings.Count ?? 0);
    public static AssessmentDetail Detail(Assessment a) => new(a.Id, a.Name, a.OrganizationName, a.AssessmentType, a.Status, a.CreatedDate, a.Result?.CompletedDate, a.Result?.OverallScore, a.Result?.Recommendation, a.Result?.ExecutiveSummary, a.Result?.ApplicationCount ?? 0, a.ErrorMessage,
        a.Result?.ScoreCards.Select(s => new ScoreCardView(s.Dimension, s.Score)).ToArray() ?? [],
        a.Result?.Findings.Select(f => new FindingView(f.Title, f.Description, f.Category, f.Severity, f.RecommendationText)).ToArray() ?? [],
        a.Result?.Recommendations.OrderBy(r => r.Priority).Select(r => new RecommendationView(r.Priority, r.Title, r.Description, r.Timeframe)).ToArray() ?? [],
        a.Artifacts.Select(x => x.OriginalFileName).ToArray(),
        a.Insights.OrderByDescending(i => i.CreatedDate).Select(Insight).ToArray(),
        a.AIQuestions.OrderByDescending(q => q.AskedAt).Select(q => new AIQuestionView(q.Id, q.Question, q.Answer, q.Status, q.AskedAt, q.ModelName, q.PromptVersion, q.TokenUsage, q.ErrorMessage)).ToArray(),
        a.RepositoryAnalysisId, a.Versions.OrderByDescending(v => v.Version).Select(v => new AssessmentVersionView(v.Version, v.RuleVersion, v.PromptVersion, v.AssessmentVersionLabel, v.CreatedAt)).ToArray(),
        a.RepositoryAnalysis is null ? null : Repository(a.RepositoryAnalysis));

    private static AssessmentInsightView Insight(AssessmentInsight i) => new(i.Id, i.Status, i.ExecutiveSummary, i.CurrentState, ReadStrings(i.KeyRisksJson), i.BusinessImpact, i.OperationalRisk, i.TechnicalDebtImpact, i.CloudReadiness, i.DeliveryVelocityImpact, i.DataAndReportingImpact, i.RiskNarrative, i.RecommendedStrategy, ReadStrings(i.RecommendedNextStepsJson), i.ExpectedOutcome, i.Confidence, i.CreatedDate, i.GeneratedAt, i.ModelName, i.PromptVersion, i.TokenUsage, i.ErrorMessage,
        i.Recommendations.OrderBy(r => r.Priority).Select(r => new AIRecommendationView(r.Priority, r.Title, r.Description, r.BusinessBenefit, r.EstimatedComplexity, r.ExpectedOutcome)).ToArray(),
        i.Roadmap.OrderBy(r => r.Sequence).Select(r => new AIInsightRoadmapView(r.Sequence, r.Period, r.ActivityType.ToString(), r.Title, r.Description, r.ExpectedOutcome)).ToArray());

    private static IReadOnlyList<string> ReadStrings(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private static RepositoryAssessmentView Repository(RepositoryAnalysis r) => new(r.Id, r.Name, r.ContentHash,
        r.Applications.Select(a => new RepositoryApplicationView(a.Name, a.RepositoryPath, a.Ecosystem, a.Runtime, ReadStrings(a.FrameworksJson), a.Database, a.Hosting, a.Containerized, a.BusinessImpact, a.TechnicalRisk)).ToArray(),
        r.Services.Select(s => new RepositoryServiceView(s.Name, s.RepositoryPath, s.Version, s.EndpointCount, ReadStrings(s.EndpointsJson), ReadStrings(s.SchemasJson), ReadStrings(s.AuthenticationJson))).ToArray(),
        r.Dependencies.Select(d => new RepositoryDependencyView(d.Name, d.Version, d.Type, d.Ecosystem, d.RepositoryPath, d.RiskLevel, d.RiskReason)).ToArray(),
        r.AgentStages.OrderBy(s => s.Sequence).Select(s => new RepositoryAgentStageView(s.Id, s.Sequence, s.AgentName, s.Status, s.OutputJson, s.StartedAt, s.CompletedAt)).ToArray());
}

public static class AssessmentComparisonService
{
    public static AssessmentComparisonView Compare(Assessment baseline, Assessment comparison)
    {
        if (baseline.AssessmentType != comparison.AssessmentType) throw new ArgumentException("Assessments must have the same type to compare.");
        if (baseline.Result is null || comparison.Result is null || baseline.Status != AssessmentStatus.Completed || comparison.Status != AssessmentStatus.Completed)
            throw new ArgumentException("Both assessments must be completed before they can be compared.");
        var first = baseline.Result.Findings.Select(Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var second = comparison.Result.Findings.Select(Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolved = baseline.Result.Findings.Where(f => !second.Contains(Identity(f))).Select(View).ToArray();
        var added = comparison.Result.Findings.Where(f => !first.Contains(Identity(f))).Select(View).ToArray();
        var delta = comparison.Result.OverallScore - baseline.Result.OverallScore;
        var severities = Enum.GetValues<FindingSeverity>().Select(severity =>
        {
            var before = baseline.Result.Findings.Count(f => f.Severity == severity);
            var after = comparison.Result.Findings.Count(f => f.Severity == severity);
            return new SeverityChangeView(severity, before, after, after - before);
        }).ToArray();
        return new AssessmentComparisonView(baseline.Id, baseline.Name, comparison.Id, comparison.Name, baseline.AssessmentType, baseline.Result.OverallScore, comparison.Result.OverallScore, delta, delta > 0 ? "Improved" : delta < 0 ? "Declined" : "Unchanged", resolved, added,
            resolved.Select(f => f.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), added.Select(f => f.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), severities, null, null);
    }

    private static string Identity(Finding f) => $"{f.Category.Trim().ToLowerInvariant()}|{f.Title.Trim().ToLowerInvariant()}";
    private static FindingChangeView View(Finding f) => new(f.Title, f.Description, f.Category, f.Severity);
}
