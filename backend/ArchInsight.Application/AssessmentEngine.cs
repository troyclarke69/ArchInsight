using System.Text.Json;
using ArchInsight.Domain;

namespace ArchInsight.Application;

public sealed record AssessmentDraft(string Name, string OrganizationName, AssessmentType AssessmentType);
public sealed record ArtifactDraft(ArtifactType Type, string FileName, string StoragePath, string MetadataJson);
public sealed record AssessmentSummary(Guid Id, string Name, string OrganizationName, AssessmentType AssessmentType, AssessmentStatus Status, DateTimeOffset CreatedDate, int? Score, string? Recommendation, int FindingsCount);
public sealed record FindingView(string Title, string Description, string Category, FindingSeverity Severity, string Recommendation);
public sealed record RecommendationView(int Priority, string Title, string Description, string Timeframe);
public sealed record ScoreCardView(string Dimension, int Score);
public sealed record AIRecommendationView(int Priority, string Title, string Description, string BusinessBenefit, string EstimatedComplexity, string ExpectedOutcome);
public sealed record AIInsightRoadmapView(int Sequence, string Period, string ActivityType, string Title, string Description, string ExpectedOutcome);
public sealed record AssessmentInsightView(Guid Id, AIInsightStatus Status, string? ExecutiveSummary, string? CurrentState, IReadOnlyList<string> KeyRisks, string? BusinessImpact, string? OperationalRisk, string? TechnicalDebtImpact, string? CloudReadiness, string? DeliveryVelocityImpact, string? DataAndReportingImpact, string? RiskNarrative, string? RecommendedStrategy, IReadOnlyList<string> RecommendedNextSteps, string? ExpectedOutcome, AIInsightConfidence? Confidence, DateTimeOffset CreatedDate, DateTimeOffset? GeneratedAt, string? ModelName, string? PromptVersion, int? TokenUsage, string? ErrorMessage, IReadOnlyList<AIRecommendationView> Recommendations, IReadOnlyList<AIInsightRoadmapView> Roadmap);
public sealed record AIQuestionView(Guid Id, string Question, string? Answer, AIQuestionStatus Status, DateTimeOffset AskedAt, string? ModelName, string? PromptVersion, int? TokenUsage, string? ErrorMessage);
public sealed record AssessmentDetail(Guid Id, string Name, string OrganizationName, AssessmentType AssessmentType, AssessmentStatus Status, DateTimeOffset CreatedDate, DateTimeOffset? CompletedDate, int? Score, string? Recommendation, string? ExecutiveSummary, int ApplicationCount, string? ErrorMessage, IReadOnlyList<ScoreCardView> ScoreCards, IReadOnlyList<FindingView> Findings, IReadOnlyList<RecommendationView> Recommendations, IReadOnlyList<string> ArtifactNames, IReadOnlyList<AssessmentInsightView> Insights, IReadOnlyList<AIQuestionView> AIQuestions);

public sealed record FindingChangeView(string Title, string Description, string Category, FindingSeverity Severity);
public sealed record SeverityChangeView(FindingSeverity Severity, int BaselineCount, int ComparisonCount, int Change);
public sealed record AssessmentComparisonView(Guid BaselineId, string BaselineName, Guid ComparisonId, string ComparisonName, AssessmentType AssessmentType, int BaselineScore, int ComparisonScore, int ScoreChange, string ScoreDirection, IReadOnlyList<FindingChangeView> ResolvedFindings, IReadOnlyList<FindingChangeView> NewFindings, IReadOnlyList<string> ImprovementAreas, IReadOnlyList<string> RegressionAreas, IReadOnlyList<SeverityChangeView> RiskMovement, string? AIExplanation, string? AIExplanationError);

public interface IAssessmentRepository
{
    Task<Assessment?> FindAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Assessment>> ListAsync(CancellationToken ct);
    Task AddAsync(Assessment item, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IArtifactStorage
{
    Task<string> StoreAsync(Guid assessmentId, string fileName, Stream content, CancellationToken ct);
}

public interface IAssessmentQueue { ValueTask EnqueueAsync(Guid id, CancellationToken ct); ValueTask<Guid> DequeueAsync(CancellationToken ct); }

public sealed class AssessmentEngine
{
    public AssessmentResult Execute(Assessment assessment)
    {
        var docs = assessment.Artifacts.Select(a => JsonDocument.Parse(a.MetadataJson)).ToList();
        try
        {
            var result = assessment.AssessmentType switch
            {
                AssessmentType.Modernization => Modernization(docs),
                AssessmentType.DataMaturity => DataMaturity(docs),
                AssessmentType.ServiceConsistency => ServiceConsistency(docs),
                _ => throw new InvalidOperationException("Unsupported assessment type.")
            };
            return result;
        }
        finally { foreach (var doc in docs) doc.Dispose(); }
    }

    private static AssessmentResult Modernization(IReadOnlyList<JsonDocument> docs)
    {
        var root = Combine(docs);
        var apps = Objects(root).Where(o => Has(o, "framework") || Has(o, "database") || Has(o, "hosting") || Has(o, "containerized") || Has(o, "architecture")).ToList();
        if (apps.Count == 0) throw new InvalidOperationException("No application inventory was recognized. Include framework, database, hosting, containerized, or architecture fields in the JSON.");
        var findings = new List<Finding>();
        var score = 100;
        if (apps.Any(a => String(a, "framework") is { } f && IsLegacyFramework(f))) AddPenalty(20, "Legacy framework", "At least one application uses a .NET Framework version below 4.8.", "Framework age", FindingSeverity.High, "Upgrade to a supported .NET runtime.");
        if (apps.Any(a => String(a, "database") is { } db && db.Contains("SQL Server 2012", StringComparison.OrdinalIgnoreCase))) AddPenalty(15, "Unsupported database engine", "SQL Server 2012 is out of extended support.", "Database", FindingSeverity.Critical, "Upgrade the database engine and validate application compatibility.");
        if (apps.Any(a => String(a, "hosting")?.Contains("IIS", StringComparison.OrdinalIgnoreCase) == true)) AddPenalty(10, "IIS hosted workload", "An application is hosted on IIS, which may constrain portability and release consistency.", "Hosting", FindingSeverity.Medium, "Assess container hosting and standardize deployment automation.");
        if (apps.Any(a => Bool(a, "containerized") == false || Bool(a, "containerized") is null)) AddPenalty(15, "No containerization evidence", "One or more applications are not reported as containerized.", "Deployment", FindingSeverity.Medium, "Containerize the application after confirming runtime compatibility.");
        if (apps.Any(a => String(a, "architecture")?.Contains("monolith", StringComparison.OrdinalIgnoreCase) == true)) AddPenalty(20, "Monolithic architecture", "A monolithic application structure can increase change coupling.", "Architecture", FindingSeverity.High, "Identify high-change boundaries before decomposing services.");
        if (apps.Any(a => Bool(a, "supportedRuntime") == true)) score += 10;
        score = Math.Clamp(score, 0, 100);
        var rec = score switch { <= 39 => "Rewrite", <= 59 => "Refactor", <= 79 => "Replatform", _ => "Lift-and-Shift" };
        var recs = new[] { ("Upgrade the application framework", "Move legacy workloads to a supported .NET runtime.", "0–3 months"), ("Containerize and standardize delivery", "Package workloads consistently and automate deployment checks.", "3–6 months"), ("Modernize the data layer", "Upgrade unsupported databases and validate recovery objectives.", "6–12 months"), ("Plan cloud migration", "Move suitable workloads after portability and security reviews.", "12+ months") };
        return Create(score, rec, $"The modernization score is {score}/100. The rule-based recommendation is {rec.ToLowerInvariant()} based on {findings.Count} detected architecture risks.", apps.Count, findings, recs, [("Overall", score)]);
        void AddPenalty(int points, string title, string description, string category, FindingSeverity severity, string recommendation) { score -= points; findings.Add(F(title, description, category, severity, recommendation)); }
    }

    private static AssessmentResult DataMaturity(IReadOnlyList<JsonDocument> docs)
    {
        var root = Combine(docs);
        var obj = Objects(root).FirstOrDefault(o => Has(o, "dataOwnersDefined") || Has(o, "catalogAvailable") || Has(o, "qualityMonitoring") || Has(o, "pipelineMonitoring") || Has(o, "warehouseAvailable") || Has(o, "structuredDatasets") || Has(o, "featureManagement") || Has(o, "vectorSearch"));
        if (obj.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("No data platform inventory was recognized. Include dataOwnersDefined, catalogAvailable, qualityMonitoring, pipelineMonitoring, or warehouseAvailable boolean fields.");
        var dimensionSignals = new Dictionary<string, bool[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Governance"] = [Bool(obj, "dataOwnersDefined") == true, Bool(obj, "catalogAvailable") == true],
            ["Quality"] = [Bool(obj, "qualityMonitoring") == true],
            ["Platform"] = [Bool(obj, "warehouseAvailable") == true],
            ["Observability"] = [Bool(obj, "pipelineMonitoring") == true],
            ["Analytics"] = [Bool(obj, "selfServiceAnalytics") == true, Bool(obj, "consistentMetrics") == true],
            ["AI Readiness"] = [Bool(obj, "structuredDatasets") == true, Bool(obj, "featureManagement") == true, Bool(obj, "vectorSearch") == true]
        };
        var cards = dimensionSignals.Select(kv => (kv.Key, kv.Value.Count(v => v) * 100 / kv.Value.Length)).ToList();
        var score = (int)Math.Round(cards.Average(c => c.Item2), MidpointRounding.AwayFromZero);
        var findings = new List<Finding>();
        AddMissing("dataOwnersDefined", "Data owners are not defined", "Governance", "Assign accountable owners for critical data domains.");
        AddMissing("catalogAvailable", "Data catalog is not available", "Governance", "Publish a searchable catalog with ownership and lineage.");
        AddMissing("qualityMonitoring", "Data quality monitoring is missing", "Quality", "Add validation rules and quality monitoring to priority datasets.");
        AddMissing("pipelineMonitoring", "Pipeline monitoring is missing", "Observability", "Track pipeline failures and define alerting and service objectives.");
        AddMissing("warehouseAvailable", "Central analytics platform is not reported", "Platform", "Establish a governed warehouse or lakehouse for shared analytics.");
        AddMissing("selfServiceAnalytics", "Self-service analytics is not reported", "Analytics", "Provide governed datasets and metrics for analyst self-service.");
        AddMissing("structuredDatasets", "Structured AI-ready datasets are not reported", "AI Readiness", "Identify and document structured datasets suitable for analytics and AI.");
        var recs = findings.Take(4).Select((f, i) => (f.Title.Replace(" is missing", ""), f.RecommendationText, i < 2 ? "0–3 months" : "3–6 months")).ToArray();
        return Create(score, "Overall average", $"The data maturity score is {score}/100 across six dimensions. Scores reflect only the evidence present in the uploaded inventory.", 0, findings, recs, cards);
        void AddMissing(string key, string title, string category, string recommendation) { if (Bool(obj, key) != true) findings.Add(F(title, $"The inventory does not report {key} as available.", category, FindingSeverity.Medium, recommendation)); }
    }

    private static AssessmentResult ServiceConsistency(IReadOnlyList<JsonDocument> docs)
    {
        var root = Combine(docs);
        var services = Objects(root).Where(o => (String(o, "name") is not null || String(Property(o, "info") ?? default, "title") is not null) && (Has(o, "paths") || Has(o, "openapi") || Has(o, "authentication") || Has(o, "auth") || Has(o, "schemas"))).ToList();
        if (services.Count < 2) throw new InvalidOperationException("At least two service entries are required. Provide a services array with name and OpenAPI or service manifest fields.");
        var findings = new List<Finding>();
        var propertyNames = services.SelectMany(ServicePropertyNames).Distinct(StringComparer.Ordinal).ToList();
        var customerNames = new[] { "CustomerId", "customerID", "cust_id" };
        if (customerNames.Count(name => propertyNames.Contains(name, StringComparer.Ordinal)) > 1) findings.Add(F("Customer identifier naming varies", $"Service contracts use inconsistent customer identifier names: {string.Join(", ", propertyNames.Where(p => customerNames.Contains(p, StringComparer.Ordinal)))}.", "Naming", FindingSeverity.Medium, "Standardize the customer identifier field across service contracts."));
        var versions = services.Select(GetApiVersion).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (versions.Count > 1) findings.Add(F("API versioning is inconsistent", $"Services report different version identifiers: {string.Join(", ", versions)}.", "API versioning", FindingSeverity.Medium, "Adopt a shared API versioning policy and document compatibility expectations."));
        var errorShapes = services.Select(o => String(o, "errorFormat") ?? InferErrorFormat(o)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (errorShapes.Count > 1) findings.Add(F("Error response formats vary", $"Service contracts expose different error formats: {string.Join(" and ", errorShapes)}.", "Error handling", FindingSeverity.High, "Publish and adopt a shared error response schema."));
        var auth = services.Select(GetAuthentication).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (auth.Count > 1) findings.Add(F("Authentication approaches are mixed", $"Services report different authentication methods: {string.Join(", ", auth)}.", "Authentication", FindingSeverity.High, "Define standard authentication and authorization requirements for services."));
        var score = Math.Clamp(100 - findings.Sum(f => f.Severity == FindingSeverity.High ? 20 : 12), 0, 100);
        var recs = findings.Select((f, i) => (f.Title, f.RecommendationText, i < 2 ? "0–3 months" : "3–6 months")).ToArray();
        return Create(score, "Consistency score", $"The service consistency score is {score}/100. {findings.Count} cross-service inconsistencies were found across {services.Count} services.", services.Count, findings, recs, [("API design", score), ("Error handling", errorShapes.Count == 1 ? 100 : 40), ("Authentication", auth.Count == 1 ? 100 : 40), ("Data contracts", propertyNames.Count < 2 ? 70 : score)]);
    }

    private static AssessmentResult Create(int score, string recommendation, string summary, int count, IEnumerable<Finding> findings, IEnumerable<(string Title, string Description, string Timeframe)> recs, IEnumerable<(string Dimension, int Score)> cards)
    {
        var result = new AssessmentResult { OverallScore = score, Recommendation = recommendation, ExecutiveSummary = summary, ApplicationCount = count };
        result.Findings = findings.ToList();
        result.Recommendations = recs.Select((r, i) => new Recommendation { Priority = i + 1, Title = r.Title, Description = r.Description, Timeframe = r.Timeframe }).ToList();
        result.ScoreCards = cards.Select(c => new ScoreCard { Dimension = c.Dimension, Score = c.Score }).ToList();
        return result;
    }
    private static Finding F(string title, string description, string category, FindingSeverity severity, string rec) => new() { Title = title, Description = description, Category = category, Severity = severity, RecommendationText = rec };
    private static JsonElement Combine(IReadOnlyList<JsonDocument> docs) => docs.Count == 1 ? docs[0].RootElement : JsonSerializer.SerializeToElement(docs.Select(d => d.RootElement).ToArray());
    private static IEnumerable<JsonElement> Objects(JsonElement root) { if (root.ValueKind == JsonValueKind.Object) { yield return root; foreach (var p in root.EnumerateObject()) foreach (var child in Objects(p.Value)) yield return child; } else if (root.ValueKind == JsonValueKind.Array) foreach (var item in root.EnumerateArray()) foreach (var child in Objects(item)) yield return child; }
    private static bool Has(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.EnumerateObject().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    private static JsonElement? Property(JsonElement e, string name) { if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value; return null; }
    private static string? String(JsonElement e, string name) { var p = Property(e, name); return p is { ValueKind: JsonValueKind.String } value ? value.GetString() : null; }
    private static bool? Bool(JsonElement e, string name) { var p = Property(e, name); return p is { ValueKind: JsonValueKind.True } ? true : p is { ValueKind: JsonValueKind.False } ? false : null; }
    private static bool IsLegacyFramework(string s) => s.Contains(".NET Framework", StringComparison.OrdinalIgnoreCase) && (Version.TryParse(s.Split(' ').LastOrDefault()?.TrimStart('v'), out var v) ? v < new Version(4, 8) : true);
    private static IEnumerable<string> ServicePropertyNames(JsonElement service) { foreach (var p in Objects(service)) { if (p.ValueKind != JsonValueKind.Object) continue; foreach (var prop in p.EnumerateObject()) if (prop.Value.ValueKind == JsonValueKind.Object && (Has(prop.Value, "type") || Has(prop.Value, "$ref"))) yield return prop.Name; } }
    private static string InferErrorFormat(JsonElement service)
    {
        var explicitFormat = String(service, "errorFormat");
        if (explicitFormat is not null) return explicitFormat;
        var keys = Objects(service).Where(o => o.ValueKind == JsonValueKind.Object).SelectMany(o => o.EnumerateObject().Select(p => p.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keys.Contains("error") && !keys.Contains("code")) return "error";
        if (keys.Contains("code") && keys.Contains("message")) return "code-message";
        return "unknown";
    }
    private static string GetApiVersion(JsonElement service)
    {
        var info = Property(service, "info");
        var version = String(service, "apiVersion") ?? (info is { } i ? String(i, "version") : null) ?? String(service, "version");
        var path = StringValues(service).Select(s => System.Text.RegularExpressions.Regex.Match(s, @"(?:^|/)v\d{1,4}(?:/|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).FirstOrDefault(m => m.Success);
        if (path is { Success: true }) version = path.Value.Trim('/');
        else if (version is not null && System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+(?:\.\d+)*$")) version = "v" + version.Split('.')[0];
        return version ?? "unversioned";
    }
    private static IEnumerable<string> StringValues(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) yield return element.GetString()!;
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) foreach (var value in StringValues(child)) yield return value;
        else if (element.ValueKind == JsonValueKind.Object) foreach (var property in element.EnumerateObject()) foreach (var value in StringValues(property.Value)) yield return value;
    }
    private static string GetAuthentication(JsonElement service)
    {
        var explicitAuth = String(service, "authentication") ?? String(service, "auth");
        if (explicitAuth is not null) return explicitAuth;
        var schemes = Property(Property(service, "components") ?? default, "securitySchemes");
        if (schemes is { ValueKind: JsonValueKind.Object } value)
        {
            var names = value.EnumerateObject().Select(p => String(p.Value, "type") == "oauth2" ? "OAuth" : String(p.Value, "scheme")?.Equals("bearer", StringComparison.OrdinalIgnoreCase) == true ? "JWT" : String(p.Value, "type") ?? p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (names.Length > 0) return string.Join("+", names);
        }
        return "None";
    }
}
