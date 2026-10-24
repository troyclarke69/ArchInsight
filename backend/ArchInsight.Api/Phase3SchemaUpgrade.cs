using ArchInsight.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ArchInsight.Api;

public static class Phase3SchemaUpgrade
{
    public static async Task ApplyAsync(AssessmentDbContext db)
    {
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            await db.Database.ExecuteSqlRawAsync(SqliteSchema);
            return;
        }
        if (db.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
            await db.Database.ExecuteSqlRawAsync(SqlServerSchema);
    }

    private const string SqliteSchema = """
        CREATE TABLE IF NOT EXISTS AssessmentInsights (
            Id TEXT NOT NULL CONSTRAINT PK_AssessmentInsights PRIMARY KEY,
            AssessmentId TEXT NOT NULL,
            Status TEXT NOT NULL,
            ExecutiveSummary TEXT NULL,
            CurrentState TEXT NULL,
            KeyRisksJson TEXT NOT NULL,
            BusinessImpact TEXT NULL,
            OperationalRisk TEXT NULL,
            TechnicalDebtImpact TEXT NULL,
            CloudReadiness TEXT NULL,
            DeliveryVelocityImpact TEXT NULL,
            DataAndReportingImpact TEXT NULL,
            RiskNarrative TEXT NULL,
            RecommendedStrategy TEXT NULL,
            RecommendedNextStepsJson TEXT NOT NULL,
            ExpectedOutcome TEXT NULL,
            Confidence TEXT NULL,
            CreatedDate TEXT NOT NULL,
            GeneratedAt TEXT NULL,
            ModelName TEXT NULL,
            PromptVersion TEXT NULL,
            TokenUsage INTEGER NULL,
            ErrorMessage TEXT NULL,
            CONSTRAINT FK_AssessmentInsights_Assessments_AssessmentId FOREIGN KEY (AssessmentId) REFERENCES Assessments (Id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_AssessmentInsights_AssessmentId_CreatedDate ON AssessmentInsights (AssessmentId, CreatedDate);

        CREATE TABLE IF NOT EXISTS AIRecommendations (
            Id TEXT NOT NULL CONSTRAINT PK_AIRecommendations PRIMARY KEY,
            InsightId TEXT NOT NULL,
            Priority INTEGER NOT NULL,
            Title TEXT NOT NULL,
            Description TEXT NOT NULL,
            BusinessBenefit TEXT NOT NULL,
            EstimatedComplexity TEXT NOT NULL,
            ExpectedOutcome TEXT NOT NULL,
            CONSTRAINT FK_AIRecommendations_AssessmentInsights_InsightId FOREIGN KEY (InsightId) REFERENCES AssessmentInsights (Id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS AIInsightRoadmapItems (
            Id TEXT NOT NULL CONSTRAINT PK_AIInsightRoadmapItems PRIMARY KEY,
            InsightId TEXT NOT NULL,
            Sequence INTEGER NOT NULL,
            Period TEXT NOT NULL,
            ActivityType TEXT NOT NULL,
            Title TEXT NOT NULL,
            Description TEXT NOT NULL,
            ExpectedOutcome TEXT NOT NULL,
            CONSTRAINT FK_AIInsightRoadmapItems_AssessmentInsights_InsightId FOREIGN KEY (InsightId) REFERENCES AssessmentInsights (Id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS AIQuestions (
            Id TEXT NOT NULL CONSTRAINT PK_AIQuestions PRIMARY KEY,
            AssessmentId TEXT NOT NULL,
            Question TEXT NOT NULL,
            Answer TEXT NULL,
            Status TEXT NOT NULL,
            AskedAt TEXT NOT NULL,
            ModelName TEXT NULL,
            PromptVersion TEXT NULL,
            TokenUsage INTEGER NULL,
            ErrorMessage TEXT NULL,
            CONSTRAINT FK_AIQuestions_Assessments_AssessmentId FOREIGN KEY (AssessmentId) REFERENCES Assessments (Id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_AIQuestions_AssessmentId_AskedAt ON AIQuestions (AssessmentId, AskedAt);
        """;

    private const string SqlServerSchema = """
        IF OBJECT_ID(N'AssessmentInsights', N'U') IS NULL
        CREATE TABLE AssessmentInsights (
            Id uniqueidentifier NOT NULL CONSTRAINT PK_AssessmentInsights PRIMARY KEY,
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
            CONSTRAINT FK_AssessmentInsights_Assessments_AssessmentId FOREIGN KEY (AssessmentId) REFERENCES Assessments (Id) ON DELETE CASCADE
        );
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AssessmentInsights_AssessmentId_CreatedDate')
            CREATE INDEX IX_AssessmentInsights_AssessmentId_CreatedDate ON AssessmentInsights (AssessmentId, CreatedDate);

        IF OBJECT_ID(N'AIRecommendations', N'U') IS NULL
        CREATE TABLE AIRecommendations (
            Id uniqueidentifier NOT NULL CONSTRAINT PK_AIRecommendations PRIMARY KEY,
            InsightId uniqueidentifier NOT NULL,
            Priority int NOT NULL,
            Title nvarchar(300) NOT NULL,
            Description nvarchar(max) NOT NULL,
            BusinessBenefit nvarchar(max) NOT NULL,
            EstimatedComplexity nvarchar(40) NOT NULL,
            ExpectedOutcome nvarchar(max) NOT NULL,
            CONSTRAINT FK_AIRecommendations_AssessmentInsights_InsightId FOREIGN KEY (InsightId) REFERENCES AssessmentInsights (Id) ON DELETE CASCADE
        );
        IF OBJECT_ID(N'AIInsightRoadmapItems', N'U') IS NULL
        CREATE TABLE AIInsightRoadmapItems (
            Id uniqueidentifier NOT NULL CONSTRAINT PK_AIInsightRoadmapItems PRIMARY KEY,
            InsightId uniqueidentifier NOT NULL,
            Sequence int NOT NULL,
            Period nvarchar(40) NOT NULL,
            ActivityType nvarchar(40) NOT NULL,
            Title nvarchar(300) NOT NULL,
            Description nvarchar(max) NOT NULL,
            ExpectedOutcome nvarchar(max) NOT NULL,
            CONSTRAINT FK_AIInsightRoadmapItems_AssessmentInsights_InsightId FOREIGN KEY (InsightId) REFERENCES AssessmentInsights (Id) ON DELETE CASCADE
        );
        IF OBJECT_ID(N'AIQuestions', N'U') IS NULL
        CREATE TABLE AIQuestions (
            Id uniqueidentifier NOT NULL CONSTRAINT PK_AIQuestions PRIMARY KEY,
            AssessmentId uniqueidentifier NOT NULL,
            Question nvarchar(1000) NOT NULL,
            Answer nvarchar(max) NULL,
            Status nvarchar(30) NOT NULL,
            AskedAt datetimeoffset NOT NULL,
            ModelName nvarchar(200) NULL,
            PromptVersion nvarchar(80) NULL,
            TokenUsage int NULL,
            ErrorMessage nvarchar(max) NULL,
            CONSTRAINT FK_AIQuestions_Assessments_AssessmentId FOREIGN KEY (AssessmentId) REFERENCES Assessments (Id) ON DELETE CASCADE
        );
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AIQuestions_AssessmentId_AskedAt')
            CREATE INDEX IX_AIQuestions_AssessmentId_AskedAt ON AIQuestions (AssessmentId, AskedAt);
        """;
}
