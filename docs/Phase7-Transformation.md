# PHASE 7 MASTER PROMPT

**Objective**

Transform ArchLens from an architecture intelligence platform into an Architecture Operating System.

Phase 7 connects architecture directly to engineering execution.

The platform should answer:

Are teams executing modernization plans?
Are risks being reduced?
Are technical debt investments producing results?
Which initiatives are stalled?
Which investments generated measurable improvement?
How does architecture health affect delivery?

The system evolves from:

Observe


to

Observe
Understand
Predict
Drive Action

Strategic Theme

Previous phases answered:

What exists?
What is wrong?
What should we do?


Phase 7 answers:

Are we actually doing it?

New Solution Project
ArchLens.Execution

Initiative Management

Create:

TransformationInitiative


Fields:

Id

Name

Description

Priority

Status

TargetCompletionDate

Owner

AssessmentId

ScenarioId


Statuses:

Planned
Approved
InProgress
Blocked
Completed
Cancelled

Recommendation → Initiative

Users can convert:

Recommendation


into

Initiative


Example:

Upgrade SQL Server 2012


becomes

Transformation Initiative

Modernization Program Dashboard

Create a new view:

Transformation Portfolio


Sections:

Planned
Active
Blocked
Completed


Metrics:

Total Initiatives

Completion %

Risk Reduced

Debt Reduced

Predictive Architecture Intelligence

This becomes one of the hero features.

Risk Forecasting

Given:

Current Risk

Technology Lifecycle

Dependencies

Governance Trends


Predict:

30 Days

90 Days

12 Months


Output:

Risk Stable

Risk Increasing

Risk Critical

Technical Debt Velocity

Track:

Debt Added

Debt Removed

Net Change


monthly.

Display:

Debt Velocity

Modernization Forecast

Example:

Current Maturity

61


Projected:

3 Months

67

6 Months

73

12 Months

84


based on active initiatives.

Engineering Impact Correlation

One of the most impressive capabilities.

Create:

EngineeringMetrics


Fields:

Deployment Frequency

Lead Time

Change Failure Rate

MTTR

Delivery Correlation Engine

Determine relationships:

Architecture Health

vs

Delivery Performance


Examples:

Applications with highest debt have longest lead times.

Services with highest inconsistency have highest incident rates.

Architecture Value Dashboard

Show leadership:

Investment

Result

Value


Example:

5 legacy systems upgraded

Risk reduction: 37%

Maturity improvement: +14

Debt reduction: -22%

Architecture Knowledge Graph

Introduce:

KnowledgeNode
KnowledgeRelationship


Node types:

Repository

Application

Service

Dependency

Technology

Database

Initiative

Finding

Recommendation

Relationship Examples
Billing API
USES
SQL Server

Customer Portal
DEPENDS_ON
Identity Service

Initiative
ADDRESSES
Finding

Architecture Copilot 3.0

Move beyond Q&A.

Support reasoning across:

Assessments

Scenarios

Governance

Initiatives

Trends


Example questions:

Which modernization initiative delivers the greatest risk reduction?

Why has architecture health declined?

Show all applications impacted by Identity Service.

Which completed projects produced measurable improvement?

Executive Board Reports

Generate:

Quarterly Architecture Review


Sections:

Current State

Major Risks

Trend Analysis

Investments

Completed Improvements

Roadmap

Forecast


Export:

PDF
PowerPoint

Strategy Advisor Agent

New agent.

Purpose:

Recommend where leadership should invest next.


Inputs:

Risk

Maturity

Dependencies

Execution Progress

Technology Lifecycle


Outputs:

Top 3 Strategic Investments

Benchmark Engine

Compare organisations against reference models.

Example:

Cloud Readiness

Security

Architecture Maturity

Observability

Governance


Output:

Emerging

Developing

Advanced

Leading


Use internal benchmark scoring initially.

No external data required.

Architecture Digital Twin

This becomes the long-term vision.

Create:

Living representation
of the software estate.


The digital twin combines:

Discovery

Assessments

Policies

Dependencies

Initiatives

Trends


into one model.

Executive Heatmaps

New visualisations:

Risk by Business Capability

Maturity by Domain

Investment by Portfolio

Technology Health by Application

AI Boardroom Mode

Generate:

CTO Briefing

CIO Briefing

Architecture Review Board Briefing

Engineering Leadership Briefing


Same underlying data.

Different audience.

Acceptance Criteria

Users can:

Manage Modernization
Recommendations
↓
Initiatives
↓
Execution Tracking

Forecast Outcomes
Risk Forecasting

Debt Forecasting

Maturity Forecasting

Measure Results
Investment Impact

Risk Reduction

Technical Debt Reduction

Governance Improvement

Navigate Architecture Knowledge Graph
Dependencies

Relationships

Impacts

Initiatives

Generate Executive Reviews
Quarterly Reports

Board Presentations

Strategic Recommendations

End State After Phase 7

The elevator pitch becomes:

ArchLens is an AI-powered Architecture Operating System that discovers software estates, assesses modernization readiness, governs architectural standards, simulates transformation outcomes, tracks execution, forecasts risk, and provides executive decision support through a continuously evolving architecture digital twin.

At this point you've moved well beyond a demo and into something that would genuinely get attention from Enterprise Architects, Principal Engineers, CTOs, and consulting firms. In fact, if you ever wanted to stop at a "final showcase" phase, I'd stop here. Phase 7 feels like a complete product vision.

---

## Demo implementation notes

The current demo adds an `ArchLens.Execution` project and a Transformation Portfolio page. Users can convert assessment recommendations into initiatives, optionally link scenarios/repositories, manage Planned/Approved/InProgress/Blocked/Completed/Cancelled status, and record measured risk/debt impact on completion. Portfolio KPIs, internal benchmark bands, debt velocity, and 30-day/90-day/12-month projections are calculated from saved governance snapshots and initiative estimates.

The knowledge graph endpoint materializes a repository view from discovered applications, services, technologies/dependencies, manually recorded dependencies, and linked initiatives. Engineering metrics (deployment frequency, lead time, change failure rate, MTTR) can be entered manually. Correlation is directional portfolio comparison; the demo does not establish causality or connect to delivery systems. Strategy Advisor and audience-specific AI Boardroom briefs use saved portfolio/assessment data when an AI provider is configured. A quarterly review can be opened in the browser print flow and saved as PDF.

Forecasts and expected/actual investment impact are demo indicators, not financial estimates or delivery guarantees. Benchmark tiers are internal fixed score bands; there is no external benchmark dataset. The browser print flow provides PDF via the user's print dialog; PowerPoint export, automated engineering telemetry, external benchmark data, and a full interactive graph canvas are not implemented.

SQLite and SQL Server receive an additive Phase 7 schema upgrade at API startup. SQL Server's standalone Phase 7 DDL is in `database/sql/006-phase7-transformation.sql`.
