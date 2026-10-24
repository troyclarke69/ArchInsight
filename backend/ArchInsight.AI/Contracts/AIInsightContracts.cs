namespace ArchInsight.AI.Contracts;

public sealed record AIProviderOptions(
    string? AzureChatCompletionsUrl,
    string? AzureApiKey,
    string? AzureDeployment,
    string? OpenAIApiKey,
    string? OpenAIModel);

public sealed record GeneratedInsight(
    string ExecutiveSummary,
    string CurrentState,
    IReadOnlyList<string> KeyRisks,
    string BusinessImpact,
    string OperationalRisk,
    string TechnicalDebtImpact,
    string CloudReadiness,
    string DeliveryVelocityImpact,
    string DataAndReportingImpact,
    string RiskNarrative,
    IReadOnlyList<string> RecommendedNextSteps,
    string ExpectedOutcome,
    IReadOnlyList<GeneratedAIRecommendation> Recommendations,
    IReadOnlyList<GeneratedRoadmapItem> Roadmap,
    string ModelName,
    string PromptVersion,
    int TokenUsage);

public sealed record GeneratedAIRecommendation(int Priority, string Title, string Description, string BusinessBenefit, string EstimatedComplexity, string ExpectedOutcome);
public sealed record GeneratedRoadmapItem(int Sequence, string Period, string ActivityType, string Title, string Description, string ExpectedOutcome);
public sealed record GeneratedAIAnswer(string Answer, string ModelName, string PromptVersion, int TokenUsage);

public interface IAIInsightGenerator
{
    bool IsConfigured { get; }
    Task<GeneratedInsight> GenerateInsightAsync(ArchInsight.Domain.Assessment assessment, CancellationToken ct);
    Task<GeneratedAIAnswer> AnswerQuestionAsync(ArchInsight.Domain.Assessment assessment, ArchInsight.Domain.AssessmentInsight? insight, string question, CancellationToken ct);
    Task<GeneratedAIAnswer> ExplainComparisonAsync(ArchInsight.Domain.Assessment first, ArchInsight.Domain.Assessment second, string deterministicComparisonJson, CancellationToken ct);
}

public interface IAIInsightQueue
{
    ValueTask EnqueueAsync(Guid assessmentId, CancellationToken ct);
    ValueTask<Guid> DequeueAsync(CancellationToken ct);
}
