using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ArchInsight.Analysis;

/// <summary>Inspects repository archives without extracting entries to disk.</summary>
public sealed class RepositoryZipAnalyzer
{
    public const long MaximumArchiveBytes = 25 * 1024 * 1024;
    private const long MaximumExpandedBytes = 100 * 1024 * 1024;
    private const int MaximumEntries = 5000;
    private const int MaximumTextEntryBytes = 8 * 1024 * 1024;
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase) { ".csproj", ".fsproj", ".vbproj", ".json", ".yaml", ".yml", ".xml", ".config", ".props", ".targets", ".txt", ".toml" };

    public async Task<RepositoryInventory> AnalyzeAsync(Stream archiveStream, string archiveName, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var copyBuffer = new byte[81920];
        int copied;
        while ((copied = await archiveStream.ReadAsync(copyBuffer.AsMemory(), ct)) > 0)
        {
            if (buffer.Length + copied > MaximumArchiveBytes) throw new InvalidDataException("Repository ZIP files must be between 1 byte and 25 MB.");
            await buffer.WriteAsync(copyBuffer.AsMemory(0, copied), ct);
        }
        if (buffer.Length == 0) throw new InvalidDataException("Repository ZIP files must be between 1 byte and 25 MB.");
        var hash = Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)))).ToLowerInvariant();
        buffer.Position = 0;
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count > MaximumEntries) throw new InvalidDataException($"Repository archive contains more than {MaximumEntries} entries.");
        long total = 0;
        try { foreach (var entry in archive.Entries) total = checked(total + entry.Length); }
        catch (OverflowException) { throw new InvalidDataException("Repository archive expanded size exceeds the analysis limit."); }
        if (total > MaximumExpandedBytes) throw new InvalidDataException("Repository archive expands beyond the 100 MB analysis limit.");

        var applications = new List<RepositoryApplication>();
        var services = new List<RepositoryService>();
        var dependencies = new List<RepositoryDependency>();
        var inventory = new List<RepositoryInventoryItem>();
        var buildWorkflows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deploymentWorkflows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var dockerFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var databases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesScanned = 0;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var path = NormalizePath(entry.FullName);
            if (path is null || path.EndsWith('/')) { if (path is null) warnings.Add($"Skipped unsafe archive path: {entry.FullName}"); continue; }
            if (path.Split('/').Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase) || part.Equals("node_modules", StringComparison.OrdinalIgnoreCase) || part.Equals("bin", StringComparison.OrdinalIgnoreCase) || part.Equals("obj", StringComparison.OrdinalIgnoreCase) || part.Equals("dist", StringComparison.OrdinalIgnoreCase) || part.Equals(".next", StringComparison.OrdinalIgnoreCase))) continue;
            if (entry.Length > MaximumTextEntryBytes && IsCandidate(path)) { warnings.Add($"Skipped oversized text file: {path}"); continue; }
            if (!IsCandidate(path)) continue;
            string text;
            try { text = await ReadTextAsync(entry, ct); }
            catch (InvalidDataException) { warnings.Add($"Skipped unreadable text file: {path}"); continue; }
            filesScanned++;
            var file = Path.GetFileName(path);
            var extension = Path.GetExtension(path);

            if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase))
                ParseDotNet(text, path, applications, dependencies, databases, inventory, warnings);
            else if (file.Equals("package.json", StringComparison.OrdinalIgnoreCase))
                ParseNode(text, path, applications, dependencies, databases, inventory, warnings);
            else if (file.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase) || file.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase))
                ParsePython(text, path, applications, dependencies, inventory);
            else if (file.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) || file.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase))
            {
                dockerFiles.Add(path); ParseDocker(text, path, dependencies, inventory);
            }
            else if (file.Equals("docker-compose.yml", StringComparison.OrdinalIgnoreCase) || file.Equals("docker-compose.yaml", StringComparison.OrdinalIgnoreCase) || file.Equals("compose.yml", StringComparison.OrdinalIgnoreCase) || file.Equals("compose.yaml", StringComparison.OrdinalIgnoreCase))
                ParseCompose(text, path, dependencies, databases, inventory);
            else if (IsWorkflow(path))
            {
                if (Regex.IsMatch(text, @"\b(deploy|release|publish|azure/webapps-deploy|aws-actions)\b", RegexOptions.IgnoreCase)) deploymentWorkflows.Add(path);
                if (Regex.IsMatch(text, @"\b(build|dotnet|npm|node|restore|test)\b", RegexOptions.IgnoreCase)) buildWorkflows.Add(path);
                inventory.Add(new RepositoryInventoryItem("Build pipeline", Path.GetFileName(path), "GitHub Actions", path));
            }
            else if (IsOpenApi(path, text))
                ParseOpenApi(text, path, services, warnings);
            else if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase) && IsConfig(path))
                ParseConfig(text, path, databases, inventory);
        }

        foreach (var db in databases) inventory.Add(new RepositoryInventoryItem("Database", db, "Detected", ""));
        var distinctDependencies = dependencies.GroupBy(d => $"{d.Ecosystem}|{d.Name}|{d.Version}|{d.Path}", StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
        var distinctApps = applications.GroupBy(a => a.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First() with { Containerized = dockerFiles.Any(d => SameOrParent(d, g.First().Path)) || g.First().Containerized, Database = databases.Count == 1 ? databases.First() : g.First().Database, Hosting = dockerFiles.Count > 0 ? "Docker" : g.First().Hosting }).ToArray();
        var repoName = Path.GetFileNameWithoutExtension(archiveName);
        if (string.IsNullOrWhiteSpace(repoName)) repoName = "Repository";
        if (distinctApps.Length == 0 && services.Count == 0) warnings.Add("No .NET projects, Node package manifests, or OpenAPI documents were recognized.");
        return new RepositoryInventory(repoName, hash, distinctApps, services, distinctDependencies, inventory.Distinct().ToArray(), buildWorkflows.ToArray(), deploymentWorkflows.ToArray(), filesScanned, warnings.Distinct().ToArray());
    }

    private static async Task<string> ReadTextAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        await using var input = entry.Open();
        using var output = new MemoryStream();
        await input.CopyToAsync(output, ct);
        if (output.Length > MaximumTextEntryBytes) throw new InvalidDataException();
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    private static string? NormalizePath(string raw)
    {
        var path = raw.Replace('\\', '/');
        if (path.StartsWith('/') || Regex.IsMatch(path, @"^[A-Za-z]:") || path.Split('/').Any(part => part == "..")) return null;
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        return path;
    }
    private static bool IsCandidate(string path) => TextExtensions.Contains(Path.GetExtension(path)) || Path.GetFileName(path).StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase) || IsWorkflow(path);
    private static bool IsWorkflow(string path) => path.Contains(".github/workflows/", StringComparison.OrdinalIgnoreCase) && (Path.GetExtension(path).Equals(".yml", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".yaml", StringComparison.OrdinalIgnoreCase));
    private static bool IsConfig(string path) => Path.GetFileName(path).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Equals("package-lock.json", StringComparison.OrdinalIgnoreCase);
    private static bool IsOpenApi(string path, string text) => (Path.GetFileName(path).Equals("openapi.json", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Equals("swagger.json", StringComparison.OrdinalIgnoreCase)) && (text.Contains("\"openapi\"", StringComparison.OrdinalIgnoreCase) || text.Contains("\"swagger\"", StringComparison.OrdinalIgnoreCase));
    private static bool SameOrParent(string file, string project) { var directory = Path.GetDirectoryName(project.Replace('\\', '/')) ?? ""; return file.StartsWith(directory, StringComparison.OrdinalIgnoreCase); }

    private static void ParseDotNet(string text, string path, List<RepositoryApplication> apps, List<RepositoryDependency> deps, HashSet<string> dbs, List<RepositoryInventoryItem> inventory, List<string> warnings)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumTextEntryBytes });
            var xml = XDocument.Load(reader);
            var frameworks = xml.Descendants().Where(x => x.Name.LocalName is "TargetFramework" or "TargetFrameworks").SelectMany(x => (x.Value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var runtime = frameworks.FirstOrDefault() ?? "unknown";
            apps.Add(new RepositoryApplication(Path.GetFileNameWithoutExtension(path), path, ".NET", runtime, frameworks, "Unknown", "Unknown", false));
            foreach (var package in xml.Descendants().Where(x => x.Name.LocalName == "PackageReference"))
            {
                var name = package.Attribute("Include")?.Value ?? package.Attribute("Update")?.Value;
                var version = package.Attribute("Version")?.Value ?? package.Elements().FirstOrDefault(x => x.Name.LocalName == "Version")?.Value ?? "unknown";
                if (string.IsNullOrWhiteSpace(name)) continue;
                deps.Add(new RepositoryDependency(name, version, ClassifyDependency(name), "NuGet", path));
                InventoryDependency(name, version, path, inventory);
                DetectDatabase(name, dbs);
            }
            if (frameworks.Length > 0) inventory.Add(new RepositoryInventoryItem("Framework", string.Join(",", frameworks), runtime, path));
        }
        catch (XmlException) { warnings.Add($"Could not parse project XML: {path}"); }
    }

    private static void ParseNode(string text, string path, List<RepositoryApplication> apps, List<RepositoryDependency> deps, HashSet<string> dbs, List<RepositoryInventoryItem> inventory, List<string> warnings)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var name = GetString(root, "name") ?? Path.GetFileName(Path.GetDirectoryName(path)) ?? "Node application";
            var nodeVersion = GetString(root, "engines") is { } ? GetNestedString(root, "engines", "node") ?? "unknown" : "unknown";
            apps.Add(new RepositoryApplication(name, path, "Node.js", nodeVersion, ["Node.js"], "Unknown", "Unknown", false));
            foreach (var group in new[] { (Key: "dependencies", Dev: false), (Key: "devDependencies", Dev: true) })
            {
                if (!root.TryGetProperty(group.Key, out var list) || list.ValueKind != JsonValueKind.Object) continue;
                foreach (var item in list.EnumerateObject())
                {
                    var version = item.Value.GetString() ?? "unknown";
                    deps.Add(new RepositoryDependency(item.Name, version, ClassifyDependency(item.Name), "npm", path + (group.Dev ? "#devDependencies" : "#dependencies")));
                    InventoryDependency(item.Name, version, path, inventory); DetectDatabase(item.Name, dbs);
                }
            }
            inventory.Add(new RepositoryInventoryItem("Runtime", "Node.js", nodeVersion, path));
        }
        catch (JsonException) { warnings.Add($"Could not parse package manifest: {path}"); }
    }

    private static void ParseDocker(string text, string path, List<RepositoryDependency> deps, List<RepositoryInventoryItem> inventory)
    {
        foreach (Match match in Regex.Matches(text, @"^\s*FROM\s+(?:--platform=\S+\s+)?(?<image>[^\s]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            var image = match.Groups["image"].Value;
            var split = image.LastIndexOf(':'); var name = split > image.LastIndexOf('/') ? image[..split] : image; var version = split > image.LastIndexOf('/') ? image[(split + 1)..] : "latest";
            deps.Add(new RepositoryDependency(name, version, "Container", "Docker", path));
            inventory.Add(new RepositoryInventoryItem("Container", name, version, path));
        }
    }
    private static void ParsePython(string text, string path, List<RepositoryApplication> apps, List<RepositoryDependency> deps, List<RepositoryInventoryItem> inventory)
    {
        var requirements = Regex.Matches(text, @"(?<name>[A-Za-z0-9_.-]+)\s*(?:==|>=|~=|\^|~|<)\s*(?<version>[A-Za-z0-9.*+_-]+)")
            .Cast<Match>().Select(m => (Name: m.Groups["name"].Value, Version: m.Groups["version"].Value)).Distinct().ToList();
        apps.Add(new RepositoryApplication(Path.GetFileName(Path.GetDirectoryName(path)) ?? "Python application", path, "Python", "Python runtime version not declared", ["Python"], "Unknown", "Unknown", false));
        foreach (var (package, version) in requirements)
        {
            deps.Add(new RepositoryDependency(package, version, ClassifyDependency(package), "PyPI", path));
            InventoryDependency(package, version, path, inventory);
        }
        inventory.Add(new RepositoryInventoryItem("Runtime", "Python", "Version not declared", path));
    }
    private static void ParseCompose(string text, string path, List<RepositoryDependency> deps, HashSet<string> dbs, List<RepositoryInventoryItem> inventory)
    {
        foreach (Match match in Regex.Matches(text, @"^\s*image\s*:\s*(?<image>[^\s#]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            var image = match.Groups["image"].Value.Trim('"', '\''); var split = image.LastIndexOf(':'); var name = split > image.LastIndexOf('/') ? image[..split] : image; var version = split > image.LastIndexOf('/') ? image[(split + 1)..] : "latest";
            deps.Add(new RepositoryDependency(name, version, "Container", "Docker Compose", path)); inventory.Add(new RepositoryInventoryItem("Container", name, version, path)); DetectDatabase(name, dbs);
        }
    }

    private static void ParseOpenApi(string text, string path, List<RepositoryService> services, List<string> warnings)
    {
        try
        {
            using var doc = JsonDocument.Parse(text); var root = doc.RootElement; var title = GetNestedString(root, "info", "title") ?? Path.GetFileNameWithoutExtension(path);
            var version = GetNestedString(root, "info", "version") ?? GetString(root, "openapi") ?? GetString(root, "swagger") ?? "unversioned";
            var endpoints = new List<string>();
            if (root.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Object)
                foreach (var route in paths.EnumerateObject()) if (route.Value.ValueKind == JsonValueKind.Object) endpoints.AddRange(route.Value.EnumerateObject().Where(m => new[] { "get", "post", "put", "patch", "delete", "head", "options", "trace" }.Contains(m.Name, StringComparer.OrdinalIgnoreCase)).Select(m => $"{m.Name.ToUpperInvariant()} {route.Name}"));
            var schemas = new List<string>();
            if (root.TryGetProperty("components", out var components) && components.TryGetProperty("schemas", out var schemaObject) && schemaObject.ValueKind == JsonValueKind.Object) schemas.AddRange(schemaObject.EnumerateObject().Select(s => s.Name));
            else if (root.TryGetProperty("definitions", out var definitions) && definitions.ValueKind == JsonValueKind.Object) schemas.AddRange(definitions.EnumerateObject().Select(s => s.Name));
            var auth = new List<string>();
            if (root.TryGetProperty("components", out components) && components.TryGetProperty("securitySchemes", out var security) && security.ValueKind == JsonValueKind.Object)
                foreach (var scheme in security.EnumerateObject()) auth.Add(GetString(scheme.Value, "type") == "oauth2" ? "OAuth2" : GetString(scheme.Value, "scheme") ?? GetString(scheme.Value, "type") ?? scheme.Name);
            if (root.TryGetProperty("security", out var globalSecurity) && globalSecurity.ValueKind == JsonValueKind.Array && globalSecurity.GetArrayLength() > 0 && auth.Count == 0) auth.Add("Security requirements defined");
            var manifest = JsonSerializer.Serialize(new { name = title, version, authentication = auth.Count == 0 ? "None" : string.Join("+", auth), schemas = schemas.ToDictionary(s => s, _ => new { type = "object" }), endpointCount = endpoints.Count, endpoints });
            services.Add(new RepositoryService(title, path, version, endpoints.Count, endpoints, schemas, auth, manifest));
        }
        catch (JsonException) { warnings.Add($"Could not parse OpenAPI JSON: {path}"); }
    }
    private static void ParseConfig(string text, string path, HashSet<string> databases, List<RepositoryInventoryItem> inventory)
    {
        foreach (Match match in Regex.Matches(text, "(?i)(Server|Data Source|Host)\\s*=\\s*([^;\\\"\\r\\n]+)"))
        {
            var endpoint = match.Groups[2].Value;
            if (endpoint.Contains("2012")) databases.Add("SQL Server 2012");
            else if (endpoint.Contains("2016")) databases.Add("SQL Server 2016");
            else if (endpoint.Contains("2019")) databases.Add("SQL Server 2019");
            else if (endpoint.Contains("2022")) databases.Add("SQL Server 2022");
            else if (endpoint.Contains("postgres", StringComparison.OrdinalIgnoreCase)) databases.Add("PostgreSQL");
            else if (endpoint.Contains("mysql", StringComparison.OrdinalIgnoreCase)) databases.Add("MySQL");
        }
        if (databases.Count > 0) inventory.Add(new RepositoryInventoryItem("Configuration", "Database connection configuration detected", "Redacted", path));
    }
    private static void DetectDatabase(string package, HashSet<string> dbs)
    {
        var p = package.ToLowerInvariant();
        if (p.Contains("sqlclient") || p.Contains("mssql")) dbs.Add("SQL Server (client library detected)");
        if (p.Contains("npgsql") || p.Contains("postgres")) dbs.Add("PostgreSQL (client library detected)");
        if (p.Contains("mysql")) dbs.Add("MySQL (client library detected)");
        if (p.Contains("mongodb")) dbs.Add("MongoDB (client library detected)");
    }
    private static string ClassifyDependency(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("sqlclient") || n.Contains("postgres") || n.Contains("mysql") || n.Contains("mongodb")) return "Database";
        if (n.StartsWith("microsoft.netcore.app") || n.StartsWith("microsoft.aspnetcore.app")) return "Framework";
        return "Library";
    }
    private static void InventoryDependency(string name, string version, string path, List<RepositoryInventoryItem> inventory) => inventory.Add(new RepositoryInventoryItem(ClassifyDependency(name), name, version, path));
    private static string? GetString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? GetNestedString(JsonElement element, string parent, string child) => element.TryGetProperty(parent, out var obj) ? GetString(obj, child) : null;
}
