namespace ArchInsight.Analysis;

public sealed record RepositoryApplication(string Name, string Path, string Ecosystem, string Runtime, IReadOnlyList<string> Frameworks, string Database, string Hosting, bool Containerized);
public sealed record RepositoryService(string Name, string Path, string Version, int EndpointCount, IReadOnlyList<string> Endpoints, IReadOnlyList<string> Schemas, IReadOnlyList<string> AuthenticationSchemes, string ServiceManifestJson);
public sealed record RepositoryDependency(string Name, string Version, string Type, string Ecosystem, string Path);
public sealed record RepositoryInventory(string Name, string ContentHash, IReadOnlyList<RepositoryApplication> Applications, IReadOnlyList<RepositoryService> Services, IReadOnlyList<RepositoryDependency> Dependencies, IReadOnlyList<RepositoryInventoryItem> Inventory, IReadOnlyList<string> BuildWorkflows, IReadOnlyList<string> DeploymentWorkflows, int FilesScanned, IReadOnlyList<string> Warnings)
{
    public string ToSbomJson() => System.Text.Json.JsonSerializer.Serialize(new { bomFormat = "CycloneDX", specVersion = "1.5", version = 1, metadata = new { timestamp = DateTimeOffset.UtcNow, component = new { type = "application", name = Name } }, components = Dependencies.Select((d, i) => new { type = d.Type.Equals("Framework", StringComparison.OrdinalIgnoreCase) ? "framework" : d.Type.Equals("Container", StringComparison.OrdinalIgnoreCase) ? "container" : "library", group = d.Ecosystem, name = d.Name, version = d.Version, bomRef = $"{d.Ecosystem}:{d.Name}:{d.Version}:{i}" }) });
}
public sealed record RepositoryInventoryItem(string Category, string Name, string Version, string Path);
