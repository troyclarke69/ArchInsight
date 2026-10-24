-- Additive Phase 3 AI insight schema for SQL Server.
IF OBJECT_ID(N'AssessmentInsights', N'U') IS NULL
CREATE TABLE AssessmentInsights (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    AssessmentId uniqueidentifier NOT NULL,
    Status nvarchar(30) NOT NULL,
    ExecutiveSummary nvarchar(max) NULL,
    CurrentState nvarchar(max) NULL,
    KeyRisksJson nvarchar(max) NOT NULL,
    BusinessImpact nvarchar(max) NULL,
    OperationalRisk nvarchar(max) NULL,
    TechnicalDebtImpact nvarchar(max) NULL,
    CloudReadiness nvarchar(max) NULL,
    DeliveryVelocityImpact nvarchar(max) NULL,
    DataAndReportingImpact nvarchar(max) NULL,
    RiskNarrative nvarchar(max) NULL,
    RecommendedStrategy nvarchar(300) NULL,
    RecommendedNextStepsJson nvarchar(max) NOT NULL,
    ExpectedOutcome nvarchar(max) NULL,
    Confidence nvarchar(20) NULL,
    CreatedDate datetimeoffset NOT NULL,
    GeneratedAt datetimeoffset NULL,
    ModelName nvarchar(200) NULL,
    PromptVersion nvarchar(80) NULL,
    TokenUsage int NULL,
    ErrorMessage nvarchar(max) NULL,
    CONSTRAINT FK_AssessmentInsights_Assessments FOREIGN KEY (AssessmentId) REFERENCES Assessments(Id) ON DELETE CASCADE
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AssessmentInsights_AssessmentId_CreatedDate')
    CREATE INDEX IX_AssessmentInsights_AssessmentId_CreatedDate ON AssessmentInsights(AssessmentId, CreatedDate);

IF OBJECT_ID(N'AIRecommendations', N'U') IS NULL
CREATE TABLE AIRecommendations (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    InsightId uniqueidentifier NOT NULL,
    Priority int NOT NULL,
    Title nvarchar(300) NOT NULL,
    Description nvarchar(max) NOT NULL,
    BusinessBenefit nvarchar(max) NOT NULL,
    EstimatedComplexity nvarchar(40) NOT NULL,
    ExpectedOutcome nvarchar(max) NOT NULL,
    CONSTRAINT FK_AIRecommendations_AssessmentInsights FOREIGN KEY (InsightId) REFERENCES AssessmentInsights(Id) ON DELETE CASCADE
);

IF OBJECT_ID(N'AIInsightRoadmapItems', N'U') IS NULL
CREATE TABLE AIInsightRoadmapItems (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    InsightId uniqueidentifier NOT NULL,
    Sequence int NOT NULL,
    Period nvarchar(40) NOT NULL,
    ActivityType nvarchar(40) NOT NULL,
    Title nvarchar(300) NOT NULL,
    Description nvarchar(max) NOT NULL,
    ExpectedOutcome nvarchar(max) NOT NULL,
    CONSTRAINT FK_AIInsightRoadmapItems_AssessmentInsights FOREIGN KEY (InsightId) REFERENCES AssessmentInsights(Id) ON DELETE CASCADE
);

IF OBJECT_ID(N'AIQuestions', N'U') IS NULL
CREATE TABLE AIQuestions (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    AssessmentId uniqueidentifier NOT NULL,
    Question nvarchar(1000) NOT NULL,
    Answer nvarchar(max) NULL,
    Status nvarchar(30) NOT NULL,
    AskedAt datetimeoffset NOT NULL,
    ModelName nvarchar(200) NULL,
    PromptVersion nvarchar(80) NULL,
    TokenUsage int NULL,
    ErrorMessage nvarchar(max) NULL,
    CONSTRAINT FK_AIQuestions_Assessments FOREIGN KEY (AssessmentId) REFERENCES Assessments(Id) ON DELETE CASCADE
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AIQuestions_AssessmentId_AskedAt')
    CREATE INDEX IX_AIQuestions_AssessmentId_AskedAt ON AIQuestions(AssessmentId, AskedAt);
