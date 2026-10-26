using ArchInsight.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ArchInsight.Api;

public static class Phase5SchemaUpgrade
{
    public static async Task ApplyAsync(AssessmentDbContext db)
    {
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            await db.Database.ExecuteSqlRawAsync(SqliteSchema);
            if (!await SqliteColumnExists(db, "Scenarios", "AdvisorExplanation"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Scenarios ADD COLUMN AdvisorExplanation TEXT NULL;");
            if (!await SqliteColumnExists(db, "Scenarios", "AdvisorModelName"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Scenarios ADD COLUMN AdvisorModelName TEXT NULL;");
            if (!await SqliteColumnExists(db, "Scenarios", "AdvisorPromptVersion"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Scenarios ADD COLUMN AdvisorPromptVersion TEXT NULL;");
            if (!await SqliteColumnExists(db, "Scenarios", "AdvisorTokenUsage"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Scenarios ADD COLUMN AdvisorTokenUsage INTEGER NULL;");
        }
        else if (db.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
            await db.Database.ExecuteSqlRawAsync(SqlServerSchema);
    }

    private static async Task<bool> SqliteColumnExists(AssessmentDbContext db, string table, string column)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        if (command.Connection!.State != System.Data.ConnectionState.Open) await command.Connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private const string SqliteSchema = """
        CREATE TABLE IF NOT EXISTS Scenarios (Id TEXT NOT NULL CONSTRAINT PK_Scenarios PRIMARY KEY, AssessmentId TEXT NOT NULL, Name TEXT NOT NULL, Description TEXT NOT NULL, CreatedDate TEXT NOT NULL, CreatedBy TEXT NOT NULL, Status TEXT NOT NULL, AdvisorExplanation TEXT NULL, AdvisorModelName TEXT NULL, AdvisorPromptVersion TEXT NULL, AdvisorTokenUsage INTEGER NULL, CONSTRAINT FK_Scenarios_Assessments_AssessmentId FOREIGN KEY (AssessmentId) REFERENCES Assessments(Id) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS ScenarioChanges (Id TEXT NOT NULL CONSTRAINT PK_ScenarioChanges PRIMARY KEY, ScenarioId TEXT NOT NULL, ChangeType TEXT NOT NULL, Target TEXT NOT NULL, OldValue TEXT NOT NULL, NewValue TEXT NOT NULL, CONSTRAINT FK_ScenarioChanges_Scenarios_ScenarioId FOREIGN KEY (ScenarioId) REFERENCES Scenarios(Id) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS ScenarioResults (Id TEXT NOT NULL CONSTRAINT PK_ScenarioResults PRIMARY KEY, ScenarioId TEXT NOT NULL, BaselineScore INTEGER NOT NULL, ProjectedScore INTEGER NOT NULL, RiskReduction INTEGER NOT NULL, CloudReadinessChange INTEGER NOT NULL, TechnicalDebtReduction INTEGER NOT NULL, ImplementationEffort INTEGER NOT NULL, BusinessImpact INTEGER NOT NULL, PriorityScore INTEGER NOT NULL, EffortLevel TEXT NOT NULL, RiskLevel TEXT NOT NULL, Summary TEXT NOT NULL, Recommendation TEXT NOT NULL, SimulatedAt TEXT NOT NULL, CONSTRAINT FK_ScenarioResults_Scenarios_ScenarioId FOREIGN KEY (ScenarioId) REFERENCES Scenarios(Id) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS ApplicationDependencies (Id TEXT NOT NULL CONSTRAINT PK_ApplicationDependencies PRIMARY KEY, RepositoryAnalysisId TEXT NOT NULL, Source TEXT NOT NULL, Target TEXT NOT NULL, DependencyType TEXT NOT NULL, CONSTRAINT FK_ApplicationDependencies_RepositoryAnalyses_RepositoryAnalysisId FOREIGN KEY (RepositoryAnalysisId) REFERENCES RepositoryAnalyses(Id) ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS IX_Scenarios_AssessmentId ON Scenarios(AssessmentId);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_ScenarioResults_ScenarioId ON ScenarioResults(ScenarioId);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_ApplicationDependencies_RepositoryAnalysisId_Source_Target ON ApplicationDependencies(RepositoryAnalysisId,Source,Target);
        """;

    private const string SqlServerSchema = """
        IF OBJECT_ID(N'Scenarios', N'U') IS NULL CREATE TABLE Scenarios (Id uniqueidentifier NOT NULL CONSTRAINT PK_Scenarios PRIMARY KEY, AssessmentId uniqueidentifier NOT NULL, Name nvarchar(160) NOT NULL, Description nvarchar(max) NOT NULL, CreatedDate datetimeoffset NOT NULL, CreatedBy nvarchar(120) NOT NULL, Status nvarchar(24) NOT NULL, AdvisorExplanation nvarchar(max) NULL, AdvisorModelName nvarchar(160) NULL, AdvisorPromptVersion nvarchar(80) NULL, AdvisorTokenUsage int NULL, CONSTRAINT FK_Scenarios_Assessments_AssessmentId FOREIGN KEY (AssessmentId) REFERENCES Assessments(Id) ON DELETE CASCADE);
        IF COL_LENGTH(N'Scenarios', N'AdvisorExplanation') IS NULL ALTER TABLE Scenarios ADD AdvisorExplanation nvarchar(max) NULL;
        IF COL_LENGTH(N'Scenarios', N'AdvisorModelName') IS NULL ALTER TABLE Scenarios ADD AdvisorModelName nvarchar(160) NULL;
        IF COL_LENGTH(N'Scenarios', N'AdvisorPromptVersion') IS NULL ALTER TABLE Scenarios ADD AdvisorPromptVersion nvarchar(80) NULL;
        IF COL_LENGTH(N'Scenarios', N'AdvisorTokenUsage') IS NULL ALTER TABLE Scenarios ADD AdvisorTokenUsage int NULL;
        IF OBJECT_ID(N'ScenarioChanges', N'U') IS NULL CREATE TABLE ScenarioChanges (Id uniqueidentifier NOT NULL CONSTRAINT PK_ScenarioChanges PRIMARY KEY, ScenarioId uniqueidentifier NOT NULL, ChangeType nvarchar(80) NOT NULL, Target nvarchar(300) NOT NULL, OldValue nvarchar(300) NOT NULL, NewValue nvarchar(300) NOT NULL, CONSTRAINT FK_ScenarioChanges_Scenarios_ScenarioId FOREIGN KEY (ScenarioId) REFERENCES Scenarios(Id) ON DELETE CASCADE);
        IF OBJECT_ID(N'ScenarioResults', N'U') IS NULL CREATE TABLE ScenarioResults (Id uniqueidentifier NOT NULL CONSTRAINT PK_ScenarioResults PRIMARY KEY, ScenarioId uniqueidentifier NOT NULL, BaselineScore int NOT NULL, ProjectedScore int NOT NULL, RiskReduction int NOT NULL, CloudReadinessChange int NOT NULL, TechnicalDebtReduction int NOT NULL, ImplementationEffort int NOT NULL, BusinessImpact int NOT NULL, PriorityScore int NOT NULL, EffortLevel nvarchar(20) NOT NULL, RiskLevel nvarchar(20) NOT NULL, Summary nvarchar(max) NOT NULL, Recommendation nvarchar(80) NOT NULL, SimulatedAt datetimeoffset NOT NULL, CONSTRAINT FK_ScenarioResults_Scenarios_ScenarioId FOREIGN KEY (ScenarioId) REFERENCES Scenarios(Id) ON DELETE CASCADE);
        IF OBJECT_ID(N'ApplicationDependencies', N'U') IS NULL CREATE TABLE ApplicationDependencies (Id uniqueidentifier NOT NULL CONSTRAINT PK_ApplicationDependencies PRIMARY KEY, RepositoryAnalysisId uniqueidentifier NOT NULL, Source nvarchar(300) NOT NULL, Target nvarchar(300) NOT NULL, DependencyType nvarchar(80) NOT NULL, CONSTRAINT FK_ApplicationDependencies_RepositoryAnalyses_RepositoryAnalysisId FOREIGN KEY (RepositoryAnalysisId) REFERENCES RepositoryAnalyses(Id) ON DELETE CASCADE);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_Scenarios_AssessmentId') CREATE INDEX IX_Scenarios_AssessmentId ON Scenarios(AssessmentId);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ScenarioResults_ScenarioId') CREATE UNIQUE INDEX IX_ScenarioResults_ScenarioId ON ScenarioResults(ScenarioId);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ApplicationDependencies_RepositoryAnalysisId_Source_Target') CREATE UNIQUE INDEX IX_ApplicationDependencies_RepositoryAnalysisId_Source_Target ON ApplicationDependencies(RepositoryAnalysisId,Source,Target);
        """;
}
