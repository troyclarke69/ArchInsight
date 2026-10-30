# ArchInsight AI

ArchInsight combines deterministic architecture assessments with an optional AI insight layer. Deterministic scores, findings, and recommendations remain the source of truth; AI adds executive context, a strategic roadmap, and question answering based only on structured assessment results.

See the [technical overview](docs/TECHNICAL-OVERVIEW.md) for the current architecture, API contracts, discovery rules, persistence behavior, and Phase 5 simulator assumptions and limitations.

## Run locally

Start the .NET 9 API in one terminal:

```powershell
dotnet run --project backend/ArchInsight.Api
```

Start Next.js in another terminal:

```powershell
npm install
npm run dev
```

The API seeds Northstar Group modernization, data maturity, and service consistency assessments on an empty database. Development uses SQLite and persists to `backend/ArchInsight.Api/archinsight.db`. The production configuration uses SQL Server; configure `ConnectionStrings:SqlServer` for the target database. SQL Server schema scripts are in `database/sql/`.

AI generation is optional and disabled until provider settings are supplied. Azure OpenAI is preferred when its endpoint, key, and deployment are configured; otherwise OpenAI is used when its key and model are configured. Keep credentials outside source control, for example with .NET user secrets or environment variables:

```powershell
$env:AI__AzureOpenAI__ChatCompletionsUrl="https://{resource}.openai.azure.com/openai/deployments/{deployment}/chat/completions?api-version={api-version}"
$env:AI__AzureOpenAI__ApiKey="..."
$env:AI__AzureOpenAI__Deployment="..."
# Or configure OpenAI instead:
$env:AI__OpenAI__ApiKey="..."
$env:AI__OpenAI__Model="..."
```

Do not configure both providers if Azure should take precedence. No credentials are committed in the example appsettings.

[OpenAI API keys](https://platform.openai.com/api-keys)

## Workflow

1. Create an assessment from the dashboard.
2. Select one assessment type and upload one or more JSON artifacts (maximum 2 MB each).
3. The API validates and stores each file under `backend/ArchInsight.Api/uploads`, then saves its metadata and parsed JSON.
4. `POST /api/assessments/{id}/run` queues work for a hosted background service. The rules engine persists a scorecard, findings, recommendations, and status.
5. Dashboard and assessment detail pages read persisted API records and refresh while an assessment or insight is running.
6. When the deterministic assessment completes, the API queues one AI insight if a provider is configured. The insight can also be regenerated manually. Dashboard reads never trigger generation.
7. The assessment detail view offers AI Q&A, and the dashboard offers historical trends and an on-demand comparison.
8. Or choose **Discover repo** and upload a ZIP (up to 25 MB). The API analyzes project/package manifests, OpenAPI JSON, Docker files, GitHub Actions, and supported Python manifests without extracting archive entries. It persists architecture inventory and an SBOM, runs specialized discovery agents, and queues modernization and service consistency assessments where enough evidence exists.
9. Open **Transformation simulator** from the dashboard navigation to create scenarios from completed assessments, compare projected outcomes, rank discovered applications, record dependency links, inspect blast radius, and review a relative investment roadmap. Scenario projections never modify assessment results.

The JSON inputs follow the requested shapes in the Phase 2 spec: modernization inventories contain an `applications` array; data inventories use boolean capability fields such as `dataOwnersDefined` and `pipelineMonitoring`; service manifests contain a `services` array. Raw OpenAPI documents are also inspected for version, schema naming, authentication schemes, and error shapes. Example files are in `public/samples/` and are downloadable from the assessment dialog.

Repository discovery is currently static and rule-based: it does not clone hosted repositories, parse OpenAPI YAML, inspect source semantics, resolve full dependency trees, or query vulnerability advisories. The generated CycloneDX file represents the components found in parsed manifests, not a complete transitive SBOM. Assessment PDF export uses the browser print dialog and its Save as PDF option.

## Current scoring assumptions

- Modernization starts at 100 and applies the listed penalties; an explicit `supportedRuntime: true` field adds 10 points, capped at 100. Missing containerization evidence is treated as not containerized. The recommendation bands follow the Phase 2 spec.
- Data maturity maps the specified boolean evidence to six dimensions. Governance has two signals, Analytics two, AI Readiness three; each present signal contributes an equal share within its dimension. Dimensions without evidence score zero, then the six dimensions are averaged.
- Service consistency deducts 12 points per medium finding and 20 per high finding, capped from 0 to 100. Version, authentication, and error format differences are reported across the uploaded services.

Repository imports add deterministic package/framework/service risks to applicable assessment findings. Security, DevOps, cloud readiness, and observability scorecards are auxiliary evidence indicators; they do not change the assessment's overall score. AI output is supplementary and is persisted separately from the deterministic result. Phase 5 simulation scores and effort are relative demo heuristics, not dollar ROI or delivery estimates; dependency links are entered by architects and need validation. The demo bootstrap uses `EnsureCreated` plus additive provider-specific schema upgrades through Phase 7; production schema changes should move to reviewed EF migrations.
# Phase 6: Architecture monitoring

Phase 6 adds a governance workflow on top of repository discovery. Upload a repository ZIP from **Monitoring** to save a new architecture snapshot, compare it with the prior snapshot, generate drift alerts, and evaluate enabled architecture policies. The page also provides technology lifecycle standards, a review queue, architecture decision records (ADRs), historical health trends, a manual pull request diff review, and an AI governance advisor grounded in saved repository/assessment data.

Monitoring is snapshot-driven: it runs when a ZIP is uploaded (or when an existing repository is first backfilled at startup). The project currently has no GitHub App/OAuth connection, hosted repository discovery, webhook, or recurring scheduler. Pull request review therefore accepts a manually supplied unified diff and does not post a GitHub review. The policy and technology-standard catalogs are seeded with editable demonstration defaults.

The API applies Phase 6 schema upgrades automatically for SQLite and SQL Server. SQL Server deployments can also apply [`database/sql/005-phase6-monitoring.sql`](database/sql/005-phase6-monitoring.sql). See [`docs/Phase6-Monitoring.md`](docs/Phase6-Monitoring.md) for the requested scope and implementation notes.

# Phase 7: Transformation execution

The **Transformation Portfolio** turns assessment recommendations into tracked initiatives, measures completed risk/debt impact, shows portfolio progress and forecasts, captures manual engineering metrics, and generates AI audience briefings. The forecast and internal benchmark use demo heuristics; delivery metrics are entered manually and comparisons are directional. SQL Server can apply [`database/sql/006-phase7-transformation.sql`](database/sql/006-phase7-transformation.sql). See [`docs/Phase7-Transformation.md`](docs/Phase7-Transformation.md) for implementation notes and remaining integration boundaries.
