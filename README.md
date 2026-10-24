# ArchInsight AI

ArchInsight combines deterministic architecture assessments with an optional AI insight layer. Deterministic scores, findings, and recommendations remain the source of truth; AI adds executive context, a strategic roadmap, and question answering based only on structured assessment results.

See the [technical overview](docs/TECHNICAL-OVERVIEW.md) for the current architecture, API contracts, assessment rules, persistence behavior, and Phase 3 boundaries.

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

**https://platform.openai.com/api-keys**

## Workflow

1. Create an assessment from the dashboard.
2. Select one assessment type and upload one or more JSON artifacts (maximum 2 MB each).
3. The API validates and stores each file under `backend/ArchInsight.Api/uploads`, then saves its metadata and parsed JSON.
4. `POST /api/assessments/{id}/run` queues work for a hosted background service. The rules engine persists a scorecard, findings, recommendations, and status.
5. Dashboard and assessment detail pages read persisted API records and refresh while an assessment or insight is running.
6. When the deterministic assessment completes, the API queues one AI insight if a provider is configured. The insight can also be regenerated manually. Dashboard reads never trigger generation.
7. The assessment detail view offers AI Q&A, and the dashboard offers historical trends and an on-demand comparison.

The JSON inputs follow the requested shapes in the Phase 2 spec: modernization inventories contain an `applications` array; data inventories use boolean capability fields such as `dataOwnersDefined` and `pipelineMonitoring`; service manifests contain a `services` array. Raw OpenAPI documents are also inspected for version, schema naming, authentication schemes, and error shapes. Example files are in `public/samples/` and are downloadable from the assessment dialog.

## Current scoring assumptions

- Modernization starts at 100 and applies the listed penalties; an explicit `supportedRuntime: true` field adds 10 points, capped at 100. Missing containerization evidence is treated as not containerized. The recommendation bands follow the Phase 2 spec.
- Data maturity maps the specified boolean evidence to six dimensions. Governance has two signals, Analytics two, AI Readiness three; each present signal contributes an equal share within its dimension. Dimensions without evidence score zero, then the six dimensions are averaged.
- Service consistency deducts 12 points per medium finding and 20 per high finding, capped from 0 to 100. Version, authentication, and error format differences are reported across the uploaded services.

These are deterministic starting rules and should be treated as assessment policy, not as AI-generated advice. Phase 3 AI output is supplementary and is persisted separately from the deterministic result. The bootstrap uses `EnsureCreated` plus an additive Phase 3 schema upgrade; production schema changes should move to reviewed EF migrations.
