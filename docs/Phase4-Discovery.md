Phase 1 was a UI prototype.
Phase 2 was a real assessment platform.
Phase 3 made it AI-powered.
Phase 4 should make it look like a product that an enterprise would actually buy.

Do not jump to more assessment types yet.

The biggest gap today is that ArchInsight is still analysing manually-created JSON artifacts.

A CTO looking at the demo will think:

"Interesting, but who is creating these JSON files?"

**Phase 4 should answer:**

"ArchInsight can discover and analyse real software estates."

**Phase 4 Objective**

Transform ArchInsight from:

Assessment Platform

into

Enterprise Architecture Discovery Platform

by introducing:

Repository Analysis
OpenAPI Discovery
Dependency Analysis
Software Bill of Materials (SBOM)
Technology Risk Analysis
Multi-Agent Assessment Pipeline

This becomes the feature that makes people stop scrolling during a demo.

# Phase 4 Prompt
Goal

Introduce automated platform discovery and agent orchestration.

Users should no longer be required to manually create assessment artifacts.

Instead:

Connect Repository
↓
Discover Services
↓
Discover APIs
↓
Discover Frameworks
↓
Discover Databases
↓
Generate Artifacts
↓
Run Assessments
↓
Generate AI Insights

New Solution Structure

* Add:

ArchInsight.Analysis
ArchInsight.Agents

Projects.

backend/

ArchInsight.Analysis
ArchInsight.Agents
ArchInsight.AI
ArchInsight.Application
ArchInsight.Domain
ArchInsight.Infrastructure
ArchInsight.Api

* Agent Architecture

Implement an orchestration layer.

Agents execute independently.

Artifact Discovery Agent

Purpose:

Normalize discovered information.

Input:

Repository
OpenAPI
Configuration
Manifest files


Output:

{
  "services": [],
  "apps": [],
  "dependencies": []
}

Modernization Agent

Consumes:

Discovered applications
Frameworks
Hosting model
Runtime versions


Produces:

Modernization inputs

Service Governance Agent

Consumes:

OpenAPI contracts
Schemas
Authentication models


Produces:

Consistency findings

Technology Risk Agent

Consumes:

Dependencies
Package versions
Framework versions


Produces:

Risk findings

Executive Advisor Agent

Consumes:

All conclusions


Produces:

Executive briefing package

Agent Runtime

Build orchestration using:

IAgent


Interface:

Task<AgentResult> ExecuteAsync(
    AgentContext context,
    CancellationToken ct);

Agent Pipeline
Discovery Agent
        ↓
Modernization Agent
        ↓
Service Governance Agent
        ↓
Technology Risk Agent
        ↓
AI Insight Agent
        ↓
Executive Advisor Agent


Persist outputs between stages.

This mimics real enterprise agent workflows.

Repository Analysis

Add ability to upload:

zip


containing:

.NET solutions
Node projects
OpenAPI specs


Maximum:

25 MB

Technology Discovery

Automatically detect:

.NET

Using:

*.csproj


Capture:

TargetFramework
PackageReference

Node

Using:

package.json


Capture:

dependencies
devDependencies

Docker

Using:

Dockerfile
docker-compose


Capture:

Containers
Images

GitHub Actions

Detect:

.github/workflows


Capture:

Build pipelines
Deployment workflows

Dependency Inventory

Create entity:

TechnologyDependency


Fields:

Name
Version
Type

Framework
Library
Database
Container
Tool

Technical Debt Rules

Examples:

.NET Framework
4.x

High Risk

SQL Server 2012
Critical Risk

Outdated Packages

Compare versions.

Flag:

Major version lag


Store finding.

OpenAPI Import

Support:

swagger.json
openapi.json


Extraction:

Endpoints
Schemas
Auth Schemes
Versions


Auto-build service assessment artifacts.

Software Bill of Materials

Generate:

SBOM


for each repository.

Store:

SoftwareInventory


Includes:

Runtime
Dependencies
Framework
Database
Infrastructure

Architecture Inventory View

Add new dashboard.

Architecture Inventory


Displays:

Applications
Services
Databases
Frameworks
Dependencies


This becomes visually impressive.

Repository Timeline

Show:

Technology Distribution

.NET Framework
.NET 6
.NET 8
Node
Python


Pie and trend visualizations.

AI Architecture Chat 2.0

Current chat answers questions.

Upgrade to:

Assessment-aware
Repository-aware
Dependency-aware


Example:

Which applications are highest risk?

Which services still use SQL Server 2012?

What blocks cloud migration?

Which repositories should be modernized first?

Executive Report Export

Generate:

PDF Executive Assessment


Sections:

Executive Summary

Assessment Results

Technology Risks

Dependency Risks

Modernization Roadmap

AI Insights

Recommendations


This is a huge interview/demo win.

Risk Heatmap

New dashboard component.

Axes:

Business Impact
vs
Technical Risk


Quadrants:

Monitor

Invest

Transform

Retire


Applications plotted automatically.

Maturity Model Expansion

Add:

Security Maturity

DevOps Maturity

Cloud Readiness

Observability Maturity


Not as standalone modules.

As dimensions.

Assessment Versioning

Create:

AssessmentVersion


Track:

Rule Version
Prompt Version
Assessment Version


This is exactly the kind of thing architects ask about during demos.

Acceptance Criteria

A user can:

Option A

Upload JSON artifacts.

(Current flow still works.)

Option B

Upload repository ZIP.

System automatically:

Discovers technologies
Discovers APIs
Discovers dependencies
Builds inventory
Generates artifacts
Runs assessments
Generates AI insights
Produces executive report

What Phase 4 Accomplishes

At the end of Phase 4 your demo narrative becomes:

"ArchInsight automatically discovers applications, APIs, frameworks, dependencies, and data platforms from real repositories. It performs deterministic architecture assessments, executes AI-powered advisory workflows using specialized agents, generates modernization roadmaps, and produces executive transformation reports."
