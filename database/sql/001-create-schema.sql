-- SQL Server schema for ArchInsight. The API can also use SQLite for a local zero-service demo.
CREATE TABLE Assessments (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    Name nvarchar(120) NOT NULL,
    OrganizationName nvarchar(120) NOT NULL,
    AssessmentType nvarchar(40) NOT NULL,
    CreatedDate datetimeoffset NOT NULL,
    Status nvarchar(20) NOT NULL,
    ErrorMessage nvarchar(max) NULL
);
CREATE INDEX IX_Assessments_CreatedDate ON Assessments(CreatedDate);

CREATE TABLE AssessmentArtifacts (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    AssessmentId uniqueidentifier NOT NULL,
    ArtifactType nvarchar(40) NOT NULL,
    OriginalFileName nvarchar(260) NOT NULL,
    StoragePath nvarchar(500) NOT NULL,
    MetadataJson nvarchar(max) NOT NULL,
    UploadedDate datetimeoffset NOT NULL,
    CONSTRAINT FK_AssessmentArtifacts_Assessments FOREIGN KEY (AssessmentId) REFERENCES Assessments(Id) ON DELETE CASCADE
);
CREATE TABLE AssessmentResults (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    AssessmentId uniqueidentifier NOT NULL UNIQUE,
    OverallScore int NOT NULL,
    Recommendation nvarchar(500) NOT NULL,
    ExecutiveSummary nvarchar(max) NOT NULL,
    ApplicationCount int NOT NULL,
    CompletedDate datetimeoffset NOT NULL,
    CONSTRAINT FK_AssessmentResults_Assessments FOREIGN KEY (AssessmentId) REFERENCES Assessments(Id) ON DELETE CASCADE
);
CREATE TABLE Findings (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    AssessmentResultId uniqueidentifier NOT NULL,
    Title nvarchar(300) NOT NULL,
    Description nvarchar(max) NOT NULL,
    Category nvarchar(120) NOT NULL,
    Severity nvarchar(20) NOT NULL,
    RecommendationText nvarchar(max) NOT NULL,
    CONSTRAINT FK_Findings_AssessmentResults FOREIGN KEY (AssessmentResultId) REFERENCES AssessmentResults(Id) ON DELETE CASCADE
);
CREATE TABLE Recommendations (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    AssessmentResultId uniqueidentifier NOT NULL,
    Priority int NOT NULL,
    Title nvarchar(300) NOT NULL,
    Description nvarchar(max) NOT NULL,
    Timeframe nvarchar(40) NOT NULL,
    CONSTRAINT FK_Recommendations_AssessmentResults FOREIGN KEY (AssessmentResultId) REFERENCES AssessmentResults(Id) ON DELETE CASCADE
);
CREATE TABLE ScoreCards (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    AssessmentResultId uniqueidentifier NOT NULL,
    Dimension nvarchar(120) NOT NULL,
    Score int NOT NULL,
    CONSTRAINT FK_ScoreCards_AssessmentResults FOREIGN KEY (AssessmentResultId) REFERENCES AssessmentResults(Id) ON DELETE CASCADE
);
