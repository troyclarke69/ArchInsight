-- Additive repository discovery, agent output, and assessment version schema.
-- The API applies the same upgrade at startup; this script is for manual SQL Server deployment.
IF COL_LENGTH(N'Assessments', N'RepositoryAnalysisId') IS NULL
    ALTER TABLE Assessments ADD RepositoryAnalysisId uniqueidentifier NULL;

IF OBJECT_ID(N'RepositoryAnalyses', N'U') IS NULL
    CREATE TABLE RepositoryAnalyses (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_RepositoryAnalyses PRIMARY KEY,
        Name nvarchar(300) NOT NULL,
        ArchivePath nvarchar(max) NOT NULL,
        ContentHash nvarchar(64) NOT NULL,
        CreatedDate datetimeoffset NOT NULL,
        SummaryJson nvarchar(max) NOT NULL,
        SbomJson nvarchar(max) NOT NULL
    );

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Assessments_RepositoryAnalyses_RepositoryAnalysisId')
    ALTER TABLE Assessments ADD CONSTRAINT FK_Assessments_RepositoryAnalyses_RepositoryAnalysisId
        FOREIGN KEY (RepositoryAnalysisId) REFERENCES RepositoryAnalyses(Id) ON DELETE SET NULL;

IF OBJECT_ID(N'DiscoveredApplications', N'U') IS NULL
    CREATE TABLE DiscoveredApplications (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveredApplications PRIMARY KEY,
        RepositoryAnalysisId uniqueidentifier NOT NULL,
        Name nvarchar(300) NOT NULL,
        RepositoryPath nvarchar(max) NOT NULL,
        Ecosystem nvarchar(80) NOT NULL,
        Runtime nvarchar(80) NOT NULL,
        FrameworksJson nvarchar(max) NOT NULL,
        [Database] nvarchar(200) NOT NULL,
        Hosting nvarchar(100) NOT NULL,
        Containerized bit NOT NULL,
        BusinessImpact int NOT NULL,
        TechnicalRisk int NOT NULL,
        CONSTRAINT FK_DiscoveredApplications_RepositoryAnalyses_RepositoryAnalysisId
            FOREIGN KEY (RepositoryAnalysisId) REFERENCES RepositoryAnalyses(Id) ON DELETE CASCADE
    );

IF OBJECT_ID(N'DiscoveredServices', N'U') IS NULL
    CREATE TABLE DiscoveredServices (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveredServices PRIMARY KEY,
        RepositoryAnalysisId uniqueidentifier NOT NULL,
        Name nvarchar(300) NOT NULL,
        RepositoryPath nvarchar(max) NOT NULL,
        Version nvarchar(100) NOT NULL,
        EndpointCount int NOT NULL,
        EndpointsJson nvarchar(max) NOT NULL,
        SchemasJson nvarchar(max) NOT NULL,
        AuthenticationJson nvarchar(max) NOT NULL,
        CONSTRAINT FK_DiscoveredServices_RepositoryAnalyses_RepositoryAnalysisId
            FOREIGN KEY (RepositoryAnalysisId) REFERENCES RepositoryAnalyses(Id) ON DELETE CASCADE
    );

IF OBJECT_ID(N'TechnologyDependencies', N'U') IS NULL
    CREATE TABLE TechnologyDependencies (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_TechnologyDependencies PRIMARY KEY,
        RepositoryAnalysisId uniqueidentifier NOT NULL,
        Name nvarchar(300) NOT NULL,
        Version nvarchar(120) NOT NULL,
        Type nvarchar(50) NOT NULL,
        Ecosystem nvarchar(60) NOT NULL,
        RepositoryPath nvarchar(max) NOT NULL,
        RiskLevel nvarchar(20) NOT NULL,
        RiskReason nvarchar(max) NOT NULL,
        CONSTRAINT FK_TechnologyDependencies_RepositoryAnalyses_RepositoryAnalysisId
            FOREIGN KEY (RepositoryAnalysisId) REFERENCES RepositoryAnalyses(Id) ON DELETE CASCADE
    );

IF OBJECT_ID(N'SoftwareInventories', N'U') IS NULL
    CREATE TABLE SoftwareInventories (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_SoftwareInventories PRIMARY KEY,
        RepositoryAnalysisId uniqueidentifier NOT NULL,
        Category nvarchar(80) NOT NULL,
        Name nvarchar(300) NOT NULL,
        Version nvarchar(120) NOT NULL,
        SourcePath nvarchar(max) NOT NULL,
        CONSTRAINT FK_SoftwareInventories_RepositoryAnalyses_RepositoryAnalysisId
            FOREIGN KEY (RepositoryAnalysisId) REFERENCES RepositoryAnalyses(Id) ON DELETE CASCADE
    );

IF OBJECT_ID(N'RepositoryAgentStages', N'U') IS NULL
    CREATE TABLE RepositoryAgentStages (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_RepositoryAgentStages PRIMARY KEY,
        RepositoryAnalysisId uniqueidentifier NOT NULL,
        Sequence int NOT NULL,
        AgentName nvarchar(100) NOT NULL,
        Status nvarchar(30) NOT NULL,
        OutputJson nvarchar(max) NOT NULL,
        StartedAt datetimeoffset NOT NULL,
        CompletedAt datetimeoffset NULL,
        CONSTRAINT FK_RepositoryAgentStages_RepositoryAnalyses_RepositoryAnalysisId
            FOREIGN KEY (RepositoryAnalysisId) REFERENCES RepositoryAnalyses(Id) ON DELETE CASCADE
    );

IF OBJECT_ID(N'AssessmentVersions', N'U') IS NULL
    CREATE TABLE AssessmentVersions (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_AssessmentVersions PRIMARY KEY,
        AssessmentId uniqueidentifier NOT NULL,
        Version int NOT NULL,
        RuleVersion nvarchar(80) NOT NULL,
        PromptVersion nvarchar(80) NOT NULL,
        AssessmentVersionLabel nvarchar(80) NOT NULL,
        CreatedAt datetimeoffset NOT NULL,
        ResultSnapshotJson nvarchar(max) NOT NULL,
        CONSTRAINT FK_AssessmentVersions_Assessments_AssessmentId
            FOREIGN KEY (AssessmentId) REFERENCES Assessments(Id) ON DELETE CASCADE
    );

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Assessments_RepositoryAnalysisId')
    CREATE INDEX IX_Assessments_RepositoryAnalysisId ON Assessments(RepositoryAnalysisId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AssessmentVersions_AssessmentId_Version')
    CREATE UNIQUE INDEX IX_AssessmentVersions_AssessmentId_Version ON AssessmentVersions(AssessmentId, Version);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TechnologyDependencies_RepositoryAnalysisId_Name_Version')
    CREATE INDEX IX_TechnologyDependencies_RepositoryAnalysisId_Name_Version ON TechnologyDependencies(RepositoryAnalysisId, Name, Version);
