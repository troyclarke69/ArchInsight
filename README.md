# ArchInsight AI

ArchInsight provides deterministic architecture assessments. Phase 2 intentionally has no LLM integration.

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

The API seeds Northstar Group modernization, data maturity, and service consistency assessments on an empty database. Development uses SQLite and persists to `backend/ArchInsight.Api/archinsight.db`. The production configuration uses SQL Server; configure `ConnectionStrings:SqlServer` for the target database. The reference schema is in [database/sql/001-create-schema.sql](database/sql/001-create-schema.sql).

## Workflow

1. Create an assessment from the dashboard.
2. Select one assessment type and upload one or more JSON artifacts (maximum 2 MB each).
3. The API validates and stores each file under `backend/ArchInsight.Api/uploads`, then saves its metadata and parsed JSON.
4. `POST /api/assessments/{id}/run` queues work for a hosted background service. The rules engine persists a scorecard, findings, recommendations, and status.
5. Dashboard and assessment detail pages read persisted API records and refresh while an assessment is running.

The JSON inputs follow the requested shapes in the Phase 2 spec: modernization inventories contain an `applications` array; data inventories use boolean capability fields such as `dataOwnersDefined` and `pipelineMonitoring`; service manifests contain a `services` array. Raw OpenAPI documents are also inspected for version, schema naming, authentication schemes, and error shapes. Example files are in `public/samples/` and are downloadable from the assessment dialog.

## Current scoring assumptions

- Modernization starts at 100 and applies the listed penalties; an explicit `supportedRuntime: true` field adds 10 points, capped at 100. Missing containerization evidence is treated as not containerized. The recommendation bands follow the Phase 2 spec.
- Data maturity maps the specified boolean evidence to six dimensions. Governance has two signals, Analytics two, AI Readiness three; each present signal contributes an equal share within its dimension. Dimensions without evidence score zero, then the six dimensions are averaged.
- Service consistency deducts 12 points per medium finding and 20 per high finding, capped from 0 to 100. Version, authentication, and error format differences are reported across the uploaded services.

These are deterministic starting rules and should be treated as assessment policy, not as AI-generated advice. The first iteration uses `EnsureCreated` for a compact demo database bootstrap; production schema changes should move to reviewed EF migrations.
