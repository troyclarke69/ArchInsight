using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchInsight.AI.Contracts;
using ArchInsight.AI.Prompts;
using ArchInsight.Domain;

namespace ArchInsight.AI.Services;

public sealed class OpenAiInsightGenerator(HttpClient http, AIProviderOptions options) : IAIInsightGenerator
{
    private const string OpenAIEndpoint = "https://api.openai.com/v1/chat/completions";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    public bool IsConfigured => ResolveProvider() is not null;

    public async Task<GeneratedInsight> GenerateInsightAsync(Assessment assessment, CancellationToken ct)
    {
        if (assessment.Result is null || assessment.Status != AssessmentStatus.Completed) throw new InvalidOperationException("AI insights require a completed deterministic assessment.");
        var confidence = EvidenceConfidenceClassifier.Classify(assessment);
        var facts = BuildFacts(assessment, confidence);
        var (content, model, tokens) = await CompleteAsync(InsightPrompts.System, InsightPrompts.InsightTask + "\n\nAuthoritative assessment facts:\n" + JsonSerializer.Serialize(facts, JsonOptions), ct);
        var output = JsonSerializer.Deserialize<InsightPayload>(content, JsonOptions) ?? throw new InvalidOperationException("The model returned an empty insight response.");
        Validate(output);
        return new GeneratedInsight(output.ExecutiveSummary.Trim(), output.CurrentState.Trim(), output.KeyRisks, output.BusinessImpact.Trim(), output.OperationalRisk.Trim(), output.TechnicalDebtImpact.Trim(), output.CloudReadiness.Trim(), output.DeliveryVelocityImpact.Trim(), output.DataAndReportingImpact.Trim(), output.RiskNarrative.Trim(), output.RecommendedNextSteps, output.ExpectedOutcome.Trim(), output.Recommendations, output.Roadmap, model, InsightPrompts.Version, tokens);
    }

    public async Task<GeneratedAIAnswer> AnswerQuestionAsync(Assessment assessment, AssessmentInsight? insight, string question, CancellationToken ct)
    {
        if (assessment.Result is null || assessment.Status != AssessmentStatus.Completed) throw new InvalidOperationException("Architecture questions require a completed assessment.");
        var facts = new { assessment = BuildFacts(assessment, EvidenceConfidenceClassifier.Classify(assessment)), insight = insight is { Status: AIInsightStatus.Completed } ? new { insight.ExecutiveSummary, insight.BusinessImpact, insight.RiskNarrative, insight.RecommendedStrategy, insight.ExpectedOutcome } : null, question };
        var (content, model, tokens) = await CompleteAsync(InsightPrompts.System, InsightPrompts.QuestionTask + "\n\nQuestion and evidence:\n" + JsonSerializer.Serialize(facts, JsonOptions), ct);
        var answer = JsonSerializer.Deserialize<TextAnswer>(content, JsonOptions)?.Answer?.Trim();
        if (string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException("The model returned an empty answer.");
        return new GeneratedAIAnswer(answer, model, InsightPrompts.Version, tokens);
    }

    public async Task<GeneratedAIAnswer> ExplainComparisonAsync(Assessment first, Assessment second, string deterministicComparisonJson, CancellationToken ct)
    {
        using var comparisonDocument = JsonDocument.Parse(deterministicComparisonJson);
        var facts = new { baseline = BuildFacts(first, EvidenceConfidenceClassifier.Classify(first)), comparison = BuildFacts(second, EvidenceConfidenceClassifier.Classify(second)), deterministicComparison = comparisonDocument.RootElement };
        var (content, model, tokens) = await CompleteAsync(InsightPrompts.System, InsightPrompts.ComparisonTask + "\n\nDeterministic comparison:\n" + JsonSerializer.Serialize(facts, JsonOptions), ct);
        var answer = JsonSerializer.Deserialize<TextAnswer>(content, JsonOptions)?.Explanation?.Trim();
        if (string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException("The model returned an empty comparison explanation.");
        return new GeneratedAIAnswer(answer, model, InsightPrompts.Version, tokens);
    }

    private object BuildFacts(Assessment assessment, AIInsightConfidence confidence) => new
    {
        assessmentType = assessment.AssessmentType.ToString(),
        assessmentName = assessment.Name,
        score = assessment.Result!.OverallScore,
        deterministicRecommendation = assessment.Result.Recommendation,
        executiveSummary = assessment.Result.ExecutiveSummary,
        systemCount = assessment.Result.ApplicationCount,
        confidence = confidence.ToString(),
        scorecards = assessment.Result.ScoreCards.Select(s => new { s.Dimension, s.Score }),
        findings = assessment.Result.Findings.Select(f => new { severity = f.Severity.ToString(), f.Title, f.Description, f.Category, deterministicAction = f.RecommendationText }),
        deterministicRecommendations = assessment.Result.Recommendations.OrderBy(r => r.Priority).Select(r => new { r.Priority, r.Title, r.Description, r.Timeframe }),
        repository = assessment.RepositoryAnalysis is null ? null : new
        {
            name = assessment.RepositoryAnalysis.Name,
            applications = assessment.RepositoryAnalysis.Applications.Take(100).Select(a => new { a.Name, a.Ecosystem, a.Runtime, frameworks = JsonSerializer.Deserialize<string[]>(a.FrameworksJson, JsonOptions), a.Database, a.Hosting, a.Containerized, a.BusinessImpact, a.TechnicalRisk }),
            services = assessment.RepositoryAnalysis.Services.Take(100).Select(s => new { s.Name, s.Version, s.EndpointCount, schemas = JsonSerializer.Deserialize<string[]>(s.SchemasJson, JsonOptions), authentication = JsonSerializer.Deserialize<string[]>(s.AuthenticationJson, JsonOptions) }),
            dependencies = assessment.RepositoryAnalysis.Dependencies.Take(300).Select(d => new { d.Name, d.Version, d.Type, d.Ecosystem, d.RiskLevel, d.RiskReason }),
            technologyInventory = assessment.RepositoryAnalysis.SoftwareInventory.Take(300).Select(i => new { i.Category, i.Name, i.Version }),
            agentStages = assessment.RepositoryAnalysis.AgentStages.OrderBy(s => s.Sequence).Select(s => new { s.AgentName, s.Status })
        }
    };

    private async Task<(string Content, string Model, int Tokens)> CompleteAsync(string system, string user, CancellationToken ct)
    {
        var provider = ResolveProvider() ?? throw new AIProviderNotConfiguredException("Configure Azure OpenAI or OpenAI credentials in the API environment.");
        using var request = new HttpRequestMessage(HttpMethod.Post, provider.Url);
        if (provider.IsAzure) request.Headers.TryAddWithoutValidation("api-key", provider.ApiKey);
        else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        request.Content = JsonContent.Create(new ChatRequest(provider.Model, [new ChatMessage("system", system), new ChatMessage("user", user)]), options: JsonOptions);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new AIProviderException($"AI provider returned {(int)response.StatusCode}: {Limit(responseText, 500)}");
        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("The AI provider returned no message content.");
        var model = root.TryGetProperty("model", out var modelValue) ? modelValue.GetString() : null;
        var tokens = root.TryGetProperty("usage", out var usage) && usage.TryGetProperty("total_tokens", out var total) && total.TryGetInt32(out var totalTokens) ? totalTokens : 0;
        return (content, model ?? provider.Model, tokens);
    }

    private Provider? ResolveProvider()
    {
        if (!string.IsNullOrWhiteSpace(options.AzureChatCompletionsUrl) && !string.IsNullOrWhiteSpace(options.AzureApiKey) && !string.IsNullOrWhiteSpace(options.AzureDeployment))
            return new Provider(options.AzureChatCompletionsUrl, options.AzureApiKey, options.AzureDeployment, true);
        if (!string.IsNullOrWhiteSpace(options.OpenAIApiKey) && !string.IsNullOrWhiteSpace(options.OpenAIModel))
            return new Provider(OpenAIEndpoint, options.OpenAIApiKey, options.OpenAIModel, false);
        return null;
    }

    private static void Validate(InsightPayload output)
    {
        var required = new[] { output.ExecutiveSummary, output.CurrentState, output.BusinessImpact, output.OperationalRisk, output.TechnicalDebtImpact, output.CloudReadiness, output.DeliveryVelocityImpact, output.DataAndReportingImpact, output.RiskNarrative, output.ExpectedOutcome };
        if (required.Any(string.IsNullOrWhiteSpace) || output.KeyRisks is null || output.RecommendedNextSteps is null || output.Recommendations is null || output.Roadmap is null)
            throw new InvalidOperationException("The model response is missing required insight fields.");
        var words = output.ExecutiveSummary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words is < 300 or > 500) throw new InvalidOperationException($"The executive summary must be 300–500 words; the model returned {words}.");
        if (output.Recommendations.Count != 5) throw new InvalidOperationException("The model must return exactly five AI recommendations.");
        if (output.Roadmap.Count != 4) throw new InvalidOperationException("The model must return four roadmap periods.");
        var periods = new[] { "0-3 months", "3-6 months", "6-12 months", "12-24 months" };
        for (var i = 0; i < periods.Length; i++) if (!string.Equals(output.Roadmap[i].Period, periods[i], StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The model returned roadmap periods in an unsupported order or format.");
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];
    private sealed record Provider(string Url, string ApiKey, string Model, bool IsAzure);
    private sealed record ChatRequest([property: JsonPropertyName("model")] string Model, [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages)
    {
        [JsonPropertyName("response_format")] public ResponseFormat ResponseFormat { get; init; } = new() { Type = "json_object" };
        [JsonPropertyName("max_completion_tokens")] public int MaxCompletionTokens { get; init; } = 3000;
    }
    private sealed record ChatMessage(string Role, string Content);
    private sealed record ResponseFormat { public string Type { get; init; } = "json_object"; }
    private sealed class InsightPayload
    {
        public string ExecutiveSummary { get; set; } = ""; public string CurrentState { get; set; } = ""; public List<string> KeyRisks { get; set; } = [];
        public string BusinessImpact { get; set; } = ""; public string OperationalRisk { get; set; } = ""; public string TechnicalDebtImpact { get; set; } = "";
        public string CloudReadiness { get; set; } = ""; public string DeliveryVelocityImpact { get; set; } = ""; public string DataAndReportingImpact { get; set; } = "";
        public string RiskNarrative { get; set; } = ""; public List<string> RecommendedNextSteps { get; set; } = []; public string ExpectedOutcome { get; set; } = "";
        public List<GeneratedAIRecommendation> Recommendations { get; set; } = []; public List<GeneratedRoadmapItem> Roadmap { get; set; } = [];
    }
    private sealed class TextAnswer { public string? Answer { get; set; } public string? Explanation { get; set; } }
}

public sealed class AIProviderNotConfiguredException(string message) : InvalidOperationException(message);
public sealed class AIProviderException(string message) : InvalidOperationException(message);
