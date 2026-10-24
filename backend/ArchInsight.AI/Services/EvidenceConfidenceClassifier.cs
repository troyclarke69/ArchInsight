using System.Text.Json;
using ArchInsight.Domain;

namespace ArchInsight.AI.Services;

public static class EvidenceConfidenceClassifier
{
    private static readonly string[] ModernizationSignals = ["framework", "database", "hosting", "containerized", "architecture", "supportedRuntime"];
    private static readonly string[] DataSignals = ["dataOwnersDefined", "catalogAvailable", "qualityMonitoring", "pipelineMonitoring", "warehouseAvailable", "selfServiceAnalytics", "consistentMetrics", "structuredDatasets", "featureManagement", "vectorSearch"];

    public static AIInsightConfidence Classify(Assessment assessment)
    {
        var documents = assessment.Artifacts.Select(a => JsonDocument.Parse(a.MetadataJson)).ToList();
        try
        {
            return assessment.AssessmentType switch
            {
                AssessmentType.Modernization => Modernization(assessment, documents),
                AssessmentType.DataMaturity => DataMaturity(documents),
                AssessmentType.ServiceConsistency => ServiceConsistency(assessment, documents),
                _ => AIInsightConfidence.Low
            };
        }
        finally { foreach (var document in documents) document.Dispose(); }
    }

    private static AIInsightConfidence Modernization(Assessment assessment, IReadOnlyList<JsonDocument> documents)
    {
        var objects = Objects(Combine(documents)).Where(o => HasAny(o, ModernizationSignals.Take(5))).ToArray();
        var expected = Math.Max(1, objects.Length * ModernizationSignals.Length);
        var actual = objects.Sum(o => ModernizationSignals.Count(signal => Has(o, signal)));
        var coverage = (double)actual / expected;
        if (objects.Length >= 3 && coverage >= .75 && assessment.Artifacts.Count >= 1) return AIInsightConfidence.High;
        if (objects.Length >= 2 && coverage >= .4) return AIInsightConfidence.Medium;
        return AIInsightConfidence.Low;
    }

    private static AIInsightConfidence DataMaturity(IReadOnlyList<JsonDocument> documents)
    {
        var root = Combine(documents);
        var obj = Objects(root).FirstOrDefault(o => HasAny(o, DataSignals));
        if (obj.ValueKind == JsonValueKind.Undefined) return AIInsightConfidence.Low;
        var coverage = (double)DataSignals.Count(signal => Has(obj, signal)) / DataSignals.Length;
        return coverage >= .8 ? AIInsightConfidence.High : coverage >= .4 ? AIInsightConfidence.Medium : AIInsightConfidence.Low;
    }

    private static AIInsightConfidence ServiceConsistency(Assessment assessment, IReadOnlyList<JsonDocument> documents)
    {
        var serviceObjects = Objects(Combine(documents)).Where(o => Has(o, "name") || Has(o, "openapi") || Has(o, "paths")).ToArray();
        var recognized = Math.Max(assessment.Result?.ApplicationCount ?? 0, serviceObjects.Length);
        var signals = serviceObjects.Sum(o => new[] { "version", "apiVersion", "authentication", "auth", "errorFormat", "schemas", "paths", "components" }.Count(s => Has(o, s)));
        var coverage = (double)signals / Math.Max(1, serviceObjects.Length * 3);
        if (recognized >= 3 && coverage >= .65) return AIInsightConfidence.High;
        if (recognized >= 2 && coverage >= .3) return AIInsightConfidence.Medium;
        return AIInsightConfidence.Low;
    }

    private static JsonElement Combine(IReadOnlyList<JsonDocument> documents) => documents.Count == 1 ? documents[0].RootElement : JsonSerializer.SerializeToElement(documents.Select(d => d.RootElement).ToArray());
    private static IEnumerable<JsonElement> Objects(JsonElement root) { if (root.ValueKind == JsonValueKind.Object) { yield return root; foreach (var p in root.EnumerateObject()) foreach (var child in Objects(p.Value)) yield return child; } else if (root.ValueKind == JsonValueKind.Array) foreach (var item in root.EnumerateArray()) foreach (var child in Objects(item)) yield return child; }
    private static bool HasAny(JsonElement element, IEnumerable<string> names) => names.Any(name => Has(element, name));
    private static bool Has(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}
