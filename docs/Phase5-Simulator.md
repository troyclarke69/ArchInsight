# PHASE 5 MASTER PROMPT

Objective

**Implement the Transformation Simulator and Portfolio Prioritization Engine.**

Phase 5 enables architects and engineering leaders to explore modernization scenarios before making investment decisions.

The platform should answer questions such as:

What happens if we upgrade to .NET 9?
What happens if we move SQL Server 2012 workloads?
Which applications should we modernize first?
What is the expected risk reduction?
What systems are impacted by a dependency change?
What delivers the greatest modernization value?

ArchInsight should evolve from:

Assessment Platform

to:

Architecture Transformation Planning Platform

Core Design Principle

Current architecture assessment remains immutable.

Phase 5 never modifies assessment results.

Instead:

Assessment
      ↓
Scenario
      ↓
Simulation
      ↓
Projected Outcome


Every scenario is derived from an assessment and can be compared to baseline.

New Projects

Add:

ArchInsight.Simulation


Optional:

ArchInsight.Portfolio

New Domain Models
Scenario
Id

AssessmentId

Name

Description

CreatedDate

CreatedBy

Status

Draft
Running
Completed
Failed

ScenarioChange

Represents a modernization action.

Id

ScenarioId

ChangeType

Target

OldValue

NewValue


Example:

Runtime
.NET Framework 4.7
.NET 9

ScenarioResult
Id

ScenarioId

ProjectedScore

RiskReduction

CloudReadinessChange

TechnicalDebtReduction

Summary

ModernizationOpportunity
ApplicationName

CurrentScore

ProjectedScore

Improvement

Priority

BusinessImpact

EngineeringEffort

Scenario Builder

Create UI:

Assessment
     ↓
Create Scenario


User sees:

Current State


and can choose:

Upgrade Runtime

Upgrade Database

Containerize

Move to Cloud

Standardize Authentication

Standardize Error Handling

Split Monolith

Remove Legacy Dependency

Simulation Engine

Implement deterministic calculations first.

No AI calculations.

AI explains outcomes.

Runtime Upgrade

Rule:

.NET Framework
↓
.NET 9


Removes:

Legacy Framework Penalty

SQL Upgrade

Rule:

SQL Server 2012
↓
SQL Server 2022


Removes:

Database Penalty

Containerization

Rule:

Containerized = True


Removes:

Container Penalty


Adds:

Cloud Readiness

Monolith Decomposition

Rule:

Monolith
↓
Service-Oriented


Removes:

Architecture Penalty

Simulation Output

Example:

Current:

Score: 48
Risk: High


Scenario:

Upgrade Runtime
Containerize


Projected:

Score: 76
Risk: Medium


Improvement:

+28 points

Side-by-Side Comparison

Create comparison page.

Show:

Current State

vs

Scenario A

vs

Scenario B


Display:

Modernization Score

Cloud Readiness

Risk Level

Technical Debt

Recommendation

Modernization ROI Engine

Do NOT estimate dollars.

Use relative scoring.

Create dimensions.

Implementation Effort
Low
Medium
High

Risk Reduction
Low
Medium
High

Business Impact
Low
Medium
High

Strategic Value
Low
Medium
High


Calculate:

Priority Score

Portfolio Prioritization

This becomes one of the strongest demo features.

Given:

10 Applications


Determine:

Which should be modernized first?


Factors:

Technical Debt

Weight:

35%

Business Impact

Weight:

30%

Dependency Complexity

Weight:

20%

Security Risk

Weight:

15%


Output:

Modernization Priority Ranking


Example:

1 Billing Platform

2 Customer Portal

3 Identity Service

4 Reporting System

Dependency Graph

Create dependency inventory.

Model:

ApplicationDependency


Fields:

Source

Target

DependencyType


Examples:

API

Database

Queue

Library

Dependency Blast Radius

One of Phase 5's hero features.

Example:

SQL Server 2012
        ↓
Billing Service
        ↓
Customer API
        ↓
Reporting Platform


User selects:

Billing Service


See:

Direct Dependencies

Downstream Systems

Affected Applications

Risk Heatmap

Add dashboard view.

Axes:

Business Impact

Technical Risk


Quadrants:

Monitor

Invest

Transform

Retire


Applications automatically placed.

AI Scenario Advisor

Phase 3 AI layer now supports:

Scenario Explanation


Prompt receives:

Current State

Scenario Changes

Projected Outcome


Produces:

Executive Explanation
Why this scenario matters

Benefits
Expected improvements

Risks
Potential challenges

Recommended Sequencing
Implementation order

Architecture Copilot Enhancements

Support:

Which modernization activity creates the greatest impact?

What is the safest path to modernization?

Which application should we modernize first?

What happens if we upgrade SQL Server?

What applications depend on Billing Service?

Executive Investment Roadmap

Generate:

Immediate

0-3 Months

3-6 Months

6-12 Months

12-24 Months


Based on:

Priority

Effort

Risk

Impact

New Dashboard Sections
Transformation Simulator

Display:

Scenarios

Projected Outcomes

Improvement

Modernization Portfolio

Display:

Ranked Applications

Risk Scores

Priority Scores

Dependency Insights

Display:

Critical Dependencies

Blast Radius

Single Points of Failure

Executive Planning

Display:

Recommended Investments

Projected Benefits

Modernization Timeline

Acceptance Criteria

User can:

Build Scenarios
Assessment
↓
Create Scenario
↓
Apply Changes
↓
Run Simulation

Compare Outcomes
Current State

vs

Scenario A

vs

Scenario B

Prioritize Portfolio
Application Ranking

Business Impact

Modernization Priority

Visualize Dependencies
Dependency Graph

Blast Radius

Critical Systems

Ask Questions
What should I modernize first?

What is the highest ROI initiative?

Which systems create the largest risk?

Resume Impact After Phase 5

At this stage, your project description becomes:

ArchInsight AI - Designed and built an AI-powered enterprise architecture intelligence platform that automatically discovers software assets, evaluates modernization readiness, analyzes architectural risk, generates executive recommendations, simulates transformation strategies, prioritizes modernization investments, and performs dependency impact analysis using deterministic assessment engines, agent workflows, and LLM-driven advisory services.

That's no longer a demo app. That's the kind of project a Principal Engineer, Enterprise Architect, or AI-native Platform Lead would be proud to showcase.