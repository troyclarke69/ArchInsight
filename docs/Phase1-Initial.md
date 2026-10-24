Master Prompt
Project Overview
Build an AI-powered enterprise assessment platform called ArchInsight AI.
ArchInsight AI evaluates an organization's software ecosystem and generates transformation recommendations, modernization roadmaps, maturity assessments, and architectural insights.
The platform should demonstrate AI-native engineering principles, multi-agent workflows, enterprise architecture analysis, data platform assessment, and executive reporting.
Target users:
CTOs
Enterprise Architects
Software Development Managers
Data Platform Leaders
Modernization Consultants
The primary objective is to help organizations understand:
the health of their application landscape
modernization opportunities
architectural risks
data maturity
service consistency
recommended next actions

Initial Modules
Module 1: Legacy Modernization Advisor
Goal
Analyze a legacy application and generate a modernization strategy.
Inputs
Architecture description
Source code snippets
Technology inventory
System dependencies
Database information
Deployment model
Assessment Areas
Technical debt
Framework age
Maintainability
Cloud readiness
Scalability
Security posture
Operational complexity
Outputs
Modernization Score (0-100)
Recommendation:
Lift-and-Shift
Replatform
Refactor
Rewrite
Risk Assessment
Estimated Effort
Modernization Roadmap
Example:
Current State
→ .NET Framework 4.6
→ IIS Hosted
→ SQL Server 2012
Recommendation
→ Refactor
Phase 1
→ Upgrade framework
Phase 2
→ Containerization
Phase 3
→ Cloud migration
Phase 4
→ Service decomposition

Module 2: Data Maturity Assessment
Goal
Assess the organization's data ecosystem and readiness for advanced analytics and AI.
Inputs
Databases
Data pipelines
Reporting platforms
Governance documentation
Data architecture descriptions
Dimensions
Data Governance
Questions:
Data owners defined?
Data catalog available?
Stewardship process exists?
Data Quality
Questions:
Monitoring?
Validation rules?
Quality metrics?
Platform
Questions:
Centralized platform?
ELT/ETL maturity?
Data lake or warehouse?
Observability
Questions:
Pipeline monitoring?
Failure alerting?
SLA tracking?
Analytics
Questions:
BI self-service?
Metrics consistency?
AI Readiness
Questions:
Structured datasets?
Feature management?
Vector search?
Knowledge repositories?
Outputs
Maturity Score
Overall: 74
Dimension Scores
Governance: 65
Quality: 72
Platform: 81
Observability: 59
Analytics: 84
AI Readiness: 63
Recommendations
Prioritized roadmap
Quick wins
Strategic investments

Module 3: Microservice Consistency Analyzer
Goal
Evaluate architectural consistency across services.
Inputs
OpenAPI specifications
Service manifests
Repository metadata
Architecture descriptions
Checks
API Design
Naming consistency
Route conventions
Versioning strategy
Error Handling
{
  "code": "VALIDATION_ERROR",
  "message": "..."
}
Validate consistency across services.
Authentication
JWT usage
OAuth implementation
Authorization patterns
Observability
Logging
Correlation IDs
Tracing
Data Contracts
Schema consistency
Shared entities
Breaking changes
Technology Drift
Framework versions
Package versions
Outputs
Consistency Score
82 / 100
Findings
14 inconsistencies detected.
Prioritized Remediation Plan
Architectural Debt Assessment

Shared AI Insight Engine
All modules use a common insight engine.
Responsibilities
Generate:
Observations
67% of services implement custom validation logic.
Risks
Single database dependency introduces operational risk.
Anomalies
Three APIs reference deprecated customer identifiers.
Recommendations
Create shared validation package.
Executive Summary
The organization demonstrates moderate architectural maturity but faces substantial modernization challenges related to technology age, operational complexity, and inconsistent service design.

AI Agent Design
Create the application using an agent-based architecture.
Intake Agent
Parses uploaded artifacts.
Produces:
{
  "systems": [],
  "services": [],
  "databases": []
}
Modernization Agent
Generates modernization recommendations.
Data Assessment Agent
Calculates maturity scores.
Service Governance Agent
Performs microservice analysis.
Insight Agent
Generates findings and recommendations.
Report Agent
Creates executive summaries.

Dashboard Experience
Landing Page
Upload Artifacts
↓
Run Assessment
↓
Review Results
↓
Export Executive Summary

Overview Dashboard
Cards:
Modernization Score
Data Maturity Score
Architecture Consistency Score
Risk Score

Findings View
Grouped by:
Critical
High
Medium
Low

Roadmap View
Timeline:
0-3 Months
3-6 Months
6-12 Months
12+ Months

Tech Stack
Frontend
Next.js
TypeScript
Tailwind
shadcn/ui
Backend
.NET 9 Web API
Data
PostgreSQL
AI
Azure OpenAI
OpenAI
Semantic Kernel
Search
pgvector
Deployment
Fly.io
