# PHASE 6 MASTER PROMPT

**Objective**

Transform the platform from an assessment and planning tool into a continuous architecture governance platform.

The system should evolve from:

Assess Once

to:

Monitor Continuously

The platform should identify architectural drift, modernization regressions, technology risks, and governance violations automatically over time.

Core Theme

Current state:

Repository
↓
Assessment
↓
Insights


Phase 6:

Repository
↓
Continuous Monitoring
↓
Drift Detection
↓
Policy Enforcement
↓
Executive Reporting

New Capability
Architecture Governance

Track:

What changed?

Is it better?

Is it worse?

Should leadership care?

New Solution Project
ArchLens.Governance

Repository Snapshots

Create:

ArchitectureSnapshot


Fields:

Id
RepositoryId
CreatedDate

ApplicationCount
ServiceCount
DependencyCount

FrameworkInventory
TechnologyInventory
RiskScore

Hash

Architecture Drift Detection

Compare snapshots.

Detect:

New Legacy Dependencies

Example:

Assessment 1

.NET 9

Assessment 2

.NET Framework 4.8


Generate:

Regression Detected

New Technical Debt

Example:

New unmanaged dependency introduced.

New Service Inconsistencies

Example:

New API naming convention violates standards.

Security Regression

Example:

JWT removed from previously secured endpoint.

Architectural Fitness Functions

Implement policy engine.

Create:

ArchitecturePolicy


Examples:

No SQL Server versions below 2019

No .NET Framework applications

JWT required

Containerization required

OpenAPI required

Observability required

Policy Evaluation

Output:

PASS

WARNING

FAIL


For every application.

Architecture Score Trend

Historical trend engine.

Track:

Modernization Score

Technical Debt

Cloud Readiness

Security

Consistency


over time.

Executive KPIs

Add dashboard:

Architecture Health

Technology Risk

Modernization Progress

---

## Implementation status

Implemented in the current project:

- `ArchLens.Governance` creates immutable snapshots from discovered repository inventory, including application/service/dependency counts, framework and technology inventory, a content hash, and health dimensions.
- Repository ZIP uploads create snapshots. Re-scanning the same repository creates a new point in the trend; startup also backfills an initial point for repositories already in the database.
- Snapshot comparison creates alerts for newly introduced legacy frameworks/dependencies, runtime regressions, removed authentication evidence, dependency pinning and naming inconsistencies, and material score regressions. Alerts can be triaged through the monitoring page.
- Six seeded fitness functions evaluate .NET framework support, SQL Server lifecycle, JWT/OAuth evidence, containerization, OpenAPI, and observability. Users can enable or disable defaults; each snapshot stores per-asset PASS/WARNING/FAIL evaluations.
- A seeded technology lifecycle catalog labels technologies approved, watch, retire, or prohibited. Snapshot health trend charts cover modernization, technical debt, cloud readiness, security, consistency, and maturity.
- The review queue combines open drift alerts, policy failures, and high risk pull-request reviews. ADRs can be created and retained with status. Manual unified diffs can be reviewed for architectural impact and receive a risk score and findings.
- The AI governance advisor can use the latest repository monitoring state, assessments, and portfolio context to explain governance concerns and suggest next steps.

The repository inventory is evidence-based but static: policy rules inspect the frameworks, dependencies, and metadata discovered by the ZIP analyzer. For example, an authentication policy reports missing JWT/OAuth evidence; it does not prove that runtime endpoints are secured. The default policies and standards are demonstration defaults and should be tailored to an organization before treating them as enforcement controls. Health dimensions are heuristics from the available static evidence.

### Integration boundary

This delivery does not include GitHub credentials or a GitHub App, repository discovery, webhooks, posting review comments, or a recurring scheduler. Scans happen on ZIP upload, and pull request diffs are submitted manually in the UI. Thus “continuous” describes persisted, comparable monitoring snapshots and governance history, not unattended polling. These integrations remain follow-on work.

The API applies an idempotent provider-specific schema upgrade at startup for SQLite and SQL Server. SQL Server installations can apply `database/sql/005-phase6-monitoring.sql` directly. Existing repositories receive a baseline snapshot at startup; no previous history can be reconstructed.

Policy Compliance

Technical Debt Trend

Technology Lifecycle Intelligence

Create:

TechnologyStandard


Example:

.NET 9

Approved

.NET Framework 4.x

Retire

SQL Server 2012

Prohibited

Technology Risk Dashboard

Show:

Approved Technologies

Watchlist

Retire

Prohibited

Architecture Review Board Mode

Create:

Review Queue


Items:

Policy Violations

High-Risk Dependencies

Technology Exceptions

Drift Alerts

AI Governance Advisor

Existing AI explains findings.

Now add:

Governance Explanations


Example:

This repository introduces three dependencies classified as retired technologies and increases the organisation's technology risk profile.

Continuous Assessment Runs

Scheduler.

Example:

Daily

Weekly

Monthly


Automatic reassessments.

GitHub Integration

Support:

GitHub Repositories


Capabilities:

Repository Discovery

Scheduled Reassessment

Pull Request Analysis

Pull Request Architecture Review

This becomes a major showcase feature.

Input:

PR Diff


Generate:

Architecture Impact

Risk Change

Dependency Changes

Policy Violations

Recommendation

Architecture Change Risk Score

Example:

Low
Medium
High
Critical


based on:

Affected Services

Dependencies

Security

Technology Standards

Evidence-Based Architecture Decisions

Create:

ArchitectureDecision


Store:

Title

Context

Decision

Consequences

Status


Essentially lightweight ADRs.

AI Decision Assistant

Example:

Should we refactor or rewrite Billing Platform?

Should we continue using SQL Server?

Should we move to Kubernetes?


AI responds using:

Existing assessments

Scenarios

Portfolio analysis

Governance status

Enterprise Dashboard

New executive homepage:

Architecture Health Over Time

Biggest Risks

Modernization Progress

Policy Compliance

Investment Priorities

Upcoming Technology Retirement Risks

Architecture Maturity Index

New composite score:

Governance

Modernization

Consistency

Security

Cloud Readiness

Operational Excellence


Output:

Architecture Maturity Index


0–100

Acceptance Criteria

Users can:

Continuously Monitor
Repositories
Applications
Dependencies

Detect Drift
Technology Changes

Policy Violations

Risk Regressions

Enforce Governance
Architecture Rules

Technology Standards

Modernization Standards

Review Trends
Health Trends

Debt Trends

Compliance Trends

Analyse Pull Requests
Risk Impact

Drift Impact

Governance Impact

Why Phase 6 Matters

Phases 1–5 answer:

"What do I have, and what should I do?"

Phase 6 answers:

"How do I prevent the organisation from falling back into technical debt?"

That's the level where the product starts resembling a blend of:

LeanIX
Ardoq
CAST
SonarQube
Architecture Review Boards

with an AI advisory layer sitting on top.

And honestly, if someone demoed a polished Phase 6 version during a Senior Architect or Principal Engineer interview, I'd expect the discussion to shift from "Can this person code?" to "Could this become a real product?" which is exactly where you want the conversation to go.
