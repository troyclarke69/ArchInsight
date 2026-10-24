Phase 2 Objective

Implement the first end-to-end assessment workflow for ArchInsight AI.

The focus of this phase is NOT LLM integration.

The focus is:

Assessment domain models
Scoring engines
Artifact ingestion
Persistence
Assessment execution pipeline
API contracts
Real dashboard data

All recommendations and findings should initially be generated from deterministic rules.

AI integration will be Phase 3.

Architecture

Create a clean architecture solution with:

src/

frontend/
  nextjs app

backend/
  ArchInsight.Api
  ArchInsight.Application
  ArchInsight.Domain
  ArchInsight.Infrastructure

database/
  sql scripts


Use:

.NET 9
Minimal APIs
MediatR
FluentValidation
Entity Framework Core
SQL Server

Next.js 15
TypeScript

Phase 2 Scope

Implement only:

Modernization Assessment
Data Maturity Assessment
Service Consistency Assessment

Core Domain Models

Create entities:

Assessment
AssessmentArtifact
AssessmentResult
Finding
Recommendation
ScoreCard


Assessment:

Id
Name
OrganizationName
AssessmentType
CreatedDate
Status

Pending
Running
Completed
Failed

Assessment Types

Create enum:

Modernization
DataMaturity
ServiceConsistency

Artifact Types

Create enum:

ArchitectureDocument
TechnologyInventory
DatabaseInventory
OpenApiSpecification
ServiceManifest
SourceCodeSummary
DataPlatformInventory

File Upload Support

Support JSON uploads only initially.

Store files:

/uploads


Persist metadata.

Example:

{
  "name": "CustomerApi",
  "framework": ".NET Framework 4.7.2",
  "database": "SQL Server 2012",
  "hosting": "IIS"
}

Assessment Execution Pipeline

When assessment starts:

Create Assessment
↓
Upload Artifacts
↓
Extract Metadata
↓
Run Assessment Engine
↓
Generate Findings
↓
Generate Recommendations
↓
Calculate Score
↓
Persist Result
↓
Update Dashboard


Implement as background service.

Modernization Engine

Create deterministic scoring.

Maximum score:

100


Rules:

Framework Age
.NET Framework < 4.8

-20

Unsupported SQL
SQL Server 2012

-15

IIS Hosting
-10

No Containerization
-15

Monolith
-20

Supported Runtime
+10

Modernization Recommendations

Map scores.

0-39
Rewrite

40-59
Refactor

60-79
Replatform

80-100
Lift-and-Shift


Generate roadmap automatically.

Example:

Refactor

Phase 1
Upgrade framework

Phase 2
Containerize application

Phase 3
Modernize data layer

Phase 4
Cloud migration

Data Maturity Engine

Dimensions:

Governance
Quality
Platform
Observability
Analytics
AI Readiness


Each scored:

0-100


Calculate overall average.

Rule examples:

Governance
Data Owners Defined

+20

Data Catalog
+20

Data Quality Monitoring
+20

Pipeline Monitoring
+20

Warehouse
+20


Missing items score zero.

Store dimension scores separately.

Service Consistency Engine

Inputs:

OpenAPI specs
Service manifests


Check:

Naming

Detect:

CustomerId
customerID
cust_id


Create finding.

API Versioning

Detect:

v1
v2
v2025


Create inconsistency finding.

Error Formats

Detect different schemas.

Example:

{
  "error": "..."
}


versus

{
  "code": "...",
  "message": "..."
}

Authentication

Check:

JWT
OAuth
None


Generate finding when mixed.

Findings

Model:

Severity

Critical
High
Medium
Low


Each finding contains:

Title
Description
Category
Severity
Recommendation

Dashboard Integration

Replace all hard-coded values.

Dashboard must call API endpoints.

Endpoints:

GET /api/assessments

GET /api/assessments/{id}

POST /api/assessments

POST /api/artifacts

POST /api/assessments/{id}/run

Assessment Details Page

Create assessment drill-down page.

Sections:

Executive Summary
Overall Score
Assessment Type
Date

Findings

Grouped:

Critical
High
Medium
Low

Recommendations

Ordered by priority.

Roadmap

Timeline view.

Seed Data

Create sample organization:

Northstar Group


Applications:

Customer Portal
Order API
Billing Service
Reporting Portal


Include intentionally poor architecture characteristics so findings appear.

Acceptance Criteria

The application should allow:

Create assessment
Upload JSON artifacts
Persist data
Execute assessment
Generate score
Generate findings
Generate recommendations
View assessment results
Refresh dashboard with actual API data
No mocked assessment results remain in UI

Important:

Do not integrate OpenAI, LangChain, Semantic Kernel, vector databases, RAG, or agent workflows yet.

Focus on creating a deterministic rules engine and assessment foundation that AI capabilities can be added to in Phase 3.

Why this is the right Phase 2

If Codex builds this well, Phase 3 becomes incredibly compelling:

Deterministic Assessment Engine
           +
      AI Insight Layer
           +
    Executive Reports
           +
       Multi-Agent Flow


That's when you can add:

OpenAI
Semantic Kernel
Agent orchestration
Executive summaries
"Ask your architecture" chat
Automated modernization plans

And by then you'll have a real product underneath instead of an AI wrapper around static data.