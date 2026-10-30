using ArchInsight.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ArchInsight.Api;

public static class Phase7SchemaUpgrade
{
    public static async Task ApplyAsync(AssessmentDbContext db)
    {
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true) await db.Database.ExecuteSqlRawAsync(Sqlite);
        else if (db.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true) await db.Database.ExecuteSqlRawAsync(SqlServer);
    }

    private const string Sqlite = """
        CREATE TABLE IF NOT EXISTS TransformationInitiatives (Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, Description TEXT NOT NULL, Priority INTEGER NOT NULL, Status TEXT NOT NULL, TargetCompletionDate TEXT NULL, Owner TEXT NOT NULL, AssessmentId TEXT NULL, ScenarioId TEXT NULL, RecommendationId TEXT NULL, RepositoryAnalysisId TEXT NULL, ExpectedRiskReduction INTEGER NOT NULL, ExpectedDebtReduction INTEGER NOT NULL, InvestmentPoints INTEGER NOT NULL, ActualRiskReduction INTEGER NOT NULL, ActualDebtReduction INTEGER NOT NULL, CreatedDate TEXT NOT NULL, FOREIGN KEY (AssessmentId) REFERENCES Assessments(Id) ON DELETE SET NULL, FOREIGN KEY (ScenarioId) REFERENCES Scenarios(Id) ON DELETE SET NULL, FOREIGN KEY (RecommendationId) REFERENCES Recommendations(Id) ON DELETE SET NULL, FOREIGN KEY (RepositoryAnalysisId) REFERENCES RepositoryAnalyses(Id) ON DELETE SET NULL);
        CREATE TABLE IF NOT EXISTS EngineeringMetrics (Id TEXT NOT NULL PRIMARY KEY, RepositoryAnalysisId TEXT NOT NULL, RecordedAt TEXT NOT NULL, DeploymentFrequencyPerMonth REAL NOT NULL, LeadTimeDays REAL NOT NULL, ChangeFailureRatePercent REAL NOT NULL, MttrHours REAL NOT NULL, Source TEXT NOT NULL, FOREIGN KEY (RepositoryAnalysisId) REFERENCES RepositoryAnalyses(Id) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS KnowledgeNodes (Id TEXT NOT NULL PRIMARY KEY, RepositoryAnalysisId TEXT NULL, NodeType TEXT NOT NULL, ExternalId TEXT NOT NULL, Label TEXT NOT NULL, MetadataJson TEXT NOT NULL, FOREIGN KEY (RepositoryAnalysisId) REFERENCES RepositoryAnalyses(Id) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS KnowledgeRelationships (Id TEXT NOT NULL PRIMARY KEY, FromNodeId TEXT NOT NULL, ToNodeId TEXT NOT NULL, RelationshipType TEXT NOT NULL, FOREIGN KEY (FromNodeId) REFERENCES KnowledgeNodes(Id) ON DELETE RESTRICT, FOREIGN KEY (ToNodeId) REFERENCES KnowledgeNodes(Id) ON DELETE RESTRICT);
        CREATE INDEX IF NOT EXISTS IX_TransformationInitiatives_Status_TargetCompletionDate ON TransformationInitiatives(Status,TargetCompletionDate);
        CREATE INDEX IF NOT EXISTS IX_EngineeringMetrics_RepositoryAnalysisId_RecordedAt ON EngineeringMetrics(RepositoryAnalysisId,RecordedAt);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_KnowledgeNodes_RepositoryAnalysisId_NodeType_ExternalId ON KnowledgeNodes(RepositoryAnalysisId,NodeType,ExternalId);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_KnowledgeRelationships_FromNodeId_ToNodeId_RelationshipType ON KnowledgeRelationships(FromNodeId,ToNodeId,RelationshipType);
        """;

    private const string SqlServer = """
        IF OBJECT_ID(N'TransformationInitiatives',N'U') IS NULL CREATE TABLE TransformationInitiatives (Id uniqueidentifier NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL, Description nvarchar(max) NOT NULL, Priority int NOT NULL, Status nvarchar(30) NOT NULL, TargetCompletionDate datetimeoffset NULL, Owner nvarchar(160) NOT NULL, AssessmentId uniqueidentifier NULL, ScenarioId uniqueidentifier NULL, RecommendationId uniqueidentifier NULL, RepositoryAnalysisId uniqueidentifier NULL, ExpectedRiskReduction int NOT NULL, ExpectedDebtReduction int NOT NULL, InvestmentPoints int NOT NULL, ActualRiskReduction int NOT NULL, ActualDebtReduction int NOT NULL, CreatedDate datetimeoffset NOT NULL, FOREIGN KEY (AssessmentId) REFERENCES Assessments(Id) ON DELETE SET NULL, FOREIGN KEY (ScenarioId) REFERENCES Scenarios(Id) ON DELETE SET NULL, FOREIGN KEY (RecommendationId) REFERENCES Recommendations(Id) ON DELETE SET NULL, FOREIGN KEY (RepositoryAnalysisId) REFERENCES RepositoryAnalyses(Id) ON DELETE SET NULL);
        IF OBJECT_ID(N'EngineeringMetrics',N'U') IS NULL CREATE TABLE EngineeringMetrics (Id uniqueidentifier NOT NULL PRIMARY KEY, RepositoryAnalysisId uniqueidentifier NOT NULL, RecordedAt datetimeoffset NOT NULL, DeploymentFrequencyPerMonth float NOT NULL, LeadTimeDays float NOT NULL, ChangeFailureRatePercent float NOT NULL, MttrHours float NOT NULL, Source nvarchar(160) NOT NULL, FOREIGN KEY (RepositoryAnalysisId) REFERENCES RepositoryAnalyses(Id) ON DELETE CASCADE);
        IF OBJECT_ID(N'KnowledgeNodes',N'U') IS NULL CREATE TABLE KnowledgeNodes (Id uniqueidentifier NOT NULL PRIMARY KEY, RepositoryAnalysisId uniqueidentifier NULL, NodeType nvarchar(40) NOT NULL, ExternalId nvarchar(200) NOT NULL, Label nvarchar(300) NOT NULL, MetadataJson nvarchar(max) NOT NULL, FOREIGN KEY (RepositoryAnalysisId) REFERENCES RepositoryAnalyses(Id) ON DELETE CASCADE);
        IF OBJECT_ID(N'KnowledgeRelationships',N'U') IS NULL CREATE TABLE KnowledgeRelationships (Id uniqueidentifier NOT NULL PRIMARY KEY, FromNodeId uniqueidentifier NOT NULL, ToNodeId uniqueidentifier NOT NULL, RelationshipType nvarchar(60) NOT NULL, FOREIGN KEY (FromNodeId) REFERENCES KnowledgeNodes(Id) ON DELETE NO ACTION, FOREIGN KEY (ToNodeId) REFERENCES KnowledgeNodes(Id) ON DELETE NO ACTION);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_TransformationInitiatives_Status_TargetCompletionDate') CREATE INDEX IX_TransformationInitiatives_Status_TargetCompletionDate ON TransformationInitiatives(Status,TargetCompletionDate);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_EngineeringMetrics_RepositoryAnalysisId_RecordedAt') CREATE INDEX IX_EngineeringMetrics_RepositoryAnalysisId_RecordedAt ON EngineeringMetrics(RepositoryAnalysisId,RecordedAt);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_KnowledgeNodes_RepositoryAnalysisId_NodeType_ExternalId') CREATE UNIQUE INDEX IX_KnowledgeNodes_RepositoryAnalysisId_NodeType_ExternalId ON KnowledgeNodes(RepositoryAnalysisId,NodeType,ExternalId);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_KnowledgeRelationships_FromNodeId_ToNodeId_RelationshipType') CREATE UNIQUE INDEX IX_KnowledgeRelationships_FromNodeId_ToNodeId_RelationshipType ON KnowledgeRelationships(FromNodeId,ToNodeId,RelationshipType);
        """;
}
