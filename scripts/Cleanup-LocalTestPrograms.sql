SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedDatabase sysname = COALESCE(
    TRY_CONVERT(sysname, SESSION_CONTEXT(N'GraduateApp.ExpectedDatabase')),
    N'GraduateAppDB');

IF @ExpectedDatabase <> N'GraduateAppDB'
    AND @ExpectedDatabase NOT LIKE N'GraduateAppCleanupScript[_]%'
BEGIN
    THROW 52100, 'Beklenen veritabanı adı izin verilen yerel geliştirme/test adlarından biri değil.', 1;
END;

IF CAST(SERVERPROPERTY('IsLocalDB') AS int) <> 1
    OR DB_NAME() <> @ExpectedDatabase
BEGIN
    THROW 52101, 'Temizlik yalnızca doğrulanmış MSSQLLocalDB hedefinde çalıştırılabilir.', 1;
END;

DECLARE @CleanupObjects TABLE
(
    ObjectID int NOT NULL PRIMARY KEY
);

INSERT INTO @CleanupObjects (ObjectID)
SELECT ObjectID
FROM
(
    VALUES
        (OBJECT_ID(N'dbo.Programs', N'U')),
        (OBJECT_ID(N'dbo.ProgramOfferings', N'U')),
        (OBJECT_ID(N'dbo.Applications', N'U')),
        (OBJECT_ID(N'dbo.ApplicationDocuments', N'U')),
        (OBJECT_ID(N'dbo.ApplicationDocumentRequirementSnapshots', N'U')),
        (OBJECT_ID(N'dbo.ApplicationEvaluations', N'U')),
        (OBJECT_ID(N'dbo.ApplicationEvaluationComponents', N'U')),
        (OBJECT_ID(N'dbo.ApplicationScoreSnapshots', N'U')),
        (OBJECT_ID(N'dbo.ApplicationStatusHistory', N'U')),
        (OBJECT_ID(N'dbo.ReferenceLetters', N'U')),
        (OBJECT_ID(N'dbo.ProgramOfferingDocumentRequirements', N'U')),
        (OBJECT_ID(N'dbo.ProgramOfferingExamRequirements', N'U')),
        (OBJECT_ID(N'dbo.ProgramOfferingEvaluationCriteria', N'U'))
) AS RequiredObjects(ObjectID)
WHERE ObjectID IS NOT NULL;

IF (SELECT COUNT(*) FROM @CleanupObjects) <> 13
BEGIN
    THROW 52102, 'Beklenen GraduateApp şeması bulunamadı; hiçbir kayıt silinmedi.', 1;
END;

IF EXISTS
(
    SELECT 1
    FROM sys.foreign_keys AS ForeignKeyMetadata
    JOIN @CleanupObjects AS ReferencedObjects
        ON ReferencedObjects.ObjectID = ForeignKeyMetadata.referenced_object_id
    LEFT JOIN @CleanupObjects AS ChildObjects
        ON ChildObjects.ObjectID = ForeignKeyMetadata.parent_object_id
    WHERE ChildObjects.ObjectID IS NULL
)
BEGIN
    THROW 52103, 'Temizlik grafiğine yeni veya beklenmeyen bir FK bağımlılığı eklendi; hiçbir kayıt silinmedi.', 1;
END;

DECLARE @TargetPrograms TABLE
(
    ProgramID int NOT NULL PRIMARY KEY,
    ProgramName nvarchar(100) NOT NULL
);
DECLARE @TargetOfferings TABLE
(
    ProgramOfferingID int NOT NULL PRIMARY KEY,
    ProgramID int NOT NULL
);
DECLARE @TargetApplications TABLE
(
    ApplicationID int NOT NULL PRIMARY KEY
);
DECLARE @TargetEvaluations TABLE
(
    ApplicationEvaluationID int NOT NULL PRIMARY KEY
);
DECLARE @TargetDocumentRequirements TABLE
(
    RequirementID int NOT NULL PRIMARY KEY
);
DECLARE @TargetCriteria TABLE
(
    CriterionID int NOT NULL PRIMARY KEY
);

INSERT INTO @TargetPrograms (ProgramID, ProgramName)
SELECT ProgramID, ProgramName
FROM dbo.Programs
WHERE ProgramName IN (N'smoke2', N'smokeprogram', N'smokes', N'UAT RC1 Program');

INSERT INTO @TargetOfferings (ProgramOfferingID, ProgramID)
SELECT Offering.ProgramOfferingID, Offering.ProgramID
FROM dbo.ProgramOfferings AS Offering
JOIN @TargetPrograms AS TargetProgram ON TargetProgram.ProgramID = Offering.ProgramID;

INSERT INTO @TargetApplications (ApplicationID)
SELECT Application.ApplicationID
FROM dbo.Applications AS Application
JOIN @TargetOfferings AS TargetOffering
    ON TargetOffering.ProgramOfferingID = Application.ProgramOfferingID;

INSERT INTO @TargetEvaluations (ApplicationEvaluationID)
SELECT Evaluation.ApplicationEvaluationID
FROM dbo.ApplicationEvaluations AS Evaluation
JOIN @TargetOfferings AS TargetOffering
    ON TargetOffering.ProgramOfferingID = Evaluation.ProgramOfferingID;

INSERT INTO @TargetDocumentRequirements (RequirementID)
SELECT Requirement.RequirementID
FROM dbo.ProgramOfferingDocumentRequirements AS Requirement
JOIN @TargetOfferings AS TargetOffering
    ON TargetOffering.ProgramOfferingID = Requirement.ProgramOfferingID;

INSERT INTO @TargetCriteria (CriterionID)
SELECT Criterion.CriterionID
FROM dbo.ProgramOfferingEvaluationCriteria AS Criterion
JOIN @TargetOfferings AS TargetOffering
    ON TargetOffering.ProgramOfferingID = Criterion.ProgramOfferingID;

SELECT TargetProgram.ProgramID,
       TargetProgram.ProgramName,
       Program.DegreeType,
       Institute.InstituteName,
       (SELECT COUNT(*)
        FROM @TargetOfferings AS TargetOffering
        WHERE TargetOffering.ProgramID = TargetProgram.ProgramID) AS ProgramOfferingCount
FROM @TargetPrograms AS TargetProgram
JOIN dbo.Programs AS Program ON Program.ProgramID = TargetProgram.ProgramID
JOIN dbo.Institutes AS Institute ON Institute.InstituteID = Program.InstituteID
ORDER BY TargetProgram.ProgramID;

SELECT N'ProgramOfferings' AS RecordType, COUNT_BIG(*) AS RecordCount FROM @TargetOfferings
UNION ALL SELECT N'Applications', COUNT_BIG(*) FROM @TargetApplications
UNION ALL SELECT N'ApplicationDocuments', COUNT_BIG(*)
    FROM dbo.ApplicationDocuments AS Document
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = Document.ApplicationID
UNION ALL SELECT N'ApplicationDocumentRequirementSnapshots', COUNT_BIG(*)
    FROM dbo.ApplicationDocumentRequirementSnapshots AS Snapshot
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = Snapshot.ApplicationID
UNION ALL SELECT N'ApplicationEvaluations', COUNT_BIG(*) FROM @TargetEvaluations
UNION ALL SELECT N'ApplicationEvaluationComponents', COUNT_BIG(*)
    FROM dbo.ApplicationEvaluationComponents AS Component
    JOIN @TargetEvaluations AS TargetEvaluation
        ON TargetEvaluation.ApplicationEvaluationID = Component.ApplicationEvaluationID
UNION ALL SELECT N'ApplicationScoreSnapshots', COUNT_BIG(*)
    FROM dbo.ApplicationScoreSnapshots AS Snapshot
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = Snapshot.ApplicationID
UNION ALL SELECT N'ApplicationStatusHistory', COUNT_BIG(*)
    FROM dbo.ApplicationStatusHistory AS History
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = History.ApplicationID
UNION ALL SELECT N'ReferenceLetters', COUNT_BIG(*)
    FROM dbo.ReferenceLetters AS Letter
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = Letter.ApplicationID
UNION ALL SELECT N'ProgramOfferingDocumentRequirements', COUNT_BIG(*) FROM @TargetDocumentRequirements
UNION ALL SELECT N'ProgramOfferingExamRequirements', COUNT_BIG(*)
    FROM dbo.ProgramOfferingExamRequirements AS Requirement
    JOIN @TargetOfferings AS TargetOffering
        ON TargetOffering.ProgramOfferingID = Requirement.ProgramOfferingID
UNION ALL SELECT N'ProgramOfferingEvaluationCriteria', COUNT_BIG(*) FROM @TargetCriteria;

IF EXISTS
(
    SELECT 1
    FROM dbo.ApplicationEvaluations AS Evaluation
    JOIN @TargetEvaluations AS TargetEvaluation
        ON TargetEvaluation.ApplicationEvaluationID = Evaluation.ApplicationEvaluationID
    LEFT JOIN @TargetApplications AS TargetApplication
        ON TargetApplication.ApplicationID = Evaluation.ApplicationID
    WHERE TargetApplication.ApplicationID IS NULL
)
    OR EXISTS
(
    SELECT 1
    FROM dbo.ApplicationEvaluations AS Evaluation
    JOIN @TargetApplications AS TargetApplication
        ON TargetApplication.ApplicationID = Evaluation.ApplicationID
    LEFT JOIN @TargetOfferings AS TargetOffering
        ON TargetOffering.ProgramOfferingID = Evaluation.ProgramOfferingID
    WHERE TargetOffering.ProgramOfferingID IS NULL
)
    OR EXISTS
(
    SELECT 1
    FROM dbo.ApplicationDocumentRequirementSnapshots AS Snapshot
    JOIN @TargetDocumentRequirements AS TargetRequirement
        ON TargetRequirement.RequirementID = Snapshot.SourceRequirementID
    LEFT JOIN @TargetApplications AS TargetApplication
        ON TargetApplication.ApplicationID = Snapshot.ApplicationID
    WHERE TargetApplication.ApplicationID IS NULL
)
    OR EXISTS
(
    SELECT 1
    FROM dbo.ApplicationEvaluationComponents AS Component
    JOIN @TargetCriteria AS TargetCriterion
        ON TargetCriterion.CriterionID = Component.SourceCriterionID
    LEFT JOIN @TargetEvaluations AS TargetEvaluation
        ON TargetEvaluation.ApplicationEvaluationID = Component.ApplicationEvaluationID
    WHERE TargetEvaluation.ApplicationEvaluationID IS NULL
)
BEGIN
    THROW 52104, 'Hedef kayıtların başka program verileriyle paylaşıldığı veya tutarsız bağlandığı görüldü; hiçbir kayıt silinmedi.', 1;
END;

IF EXISTS
(
    SELECT 1
    FROM dbo.ApplicationDocuments AS Document
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = Document.ApplicationID
)
    OR EXISTS
(
    SELECT 1
    FROM dbo.ReferenceLetters AS Letter
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = Letter.ApplicationID
    WHERE NULLIF(LTRIM(RTRIM(Letter.FilePath)), N'') IS NOT NULL
)
BEGIN
    SELECT Document.DocumentID, Document.ApplicationID, Document.ObjectKey
    FROM dbo.ApplicationDocuments AS Document
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = Document.ApplicationID;

    SELECT Letter.ReferenceID, Letter.ApplicationID, Letter.FilePath
    FROM dbo.ReferenceLetters AS Letter
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = Letter.ApplicationID
    WHERE NULLIF(LTRIM(RTRIM(Letter.FilePath)), N'') IS NOT NULL;

    THROW 52105, 'Dosya-backed belge kaydı bulundu; storage temizliği yapılmadan SQL temizliği durduruldu.', 1;
END;

DECLARE @DeletedCounts TABLE
(
    RecordType nvarchar(100) NOT NULL,
    RecordCount int NOT NULL
);

BEGIN TRY
    BEGIN TRANSACTION;

    DELETE Document
    FROM dbo.ApplicationDocuments AS Document
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = Document.ApplicationID;
    INSERT INTO @DeletedCounts VALUES (N'ApplicationDocuments', @@ROWCOUNT);

    DELETE Component
    FROM dbo.ApplicationEvaluationComponents AS Component
    JOIN @TargetEvaluations AS TargetEvaluation
        ON TargetEvaluation.ApplicationEvaluationID = Component.ApplicationEvaluationID;
    INSERT INTO @DeletedCounts VALUES (N'ApplicationEvaluationComponents', @@ROWCOUNT);

    DELETE Evaluation
    FROM dbo.ApplicationEvaluations AS Evaluation
    JOIN @TargetEvaluations AS TargetEvaluation
        ON TargetEvaluation.ApplicationEvaluationID = Evaluation.ApplicationEvaluationID;
    INSERT INTO @DeletedCounts VALUES (N'ApplicationEvaluations', @@ROWCOUNT);

    DELETE Snapshot
    FROM dbo.ApplicationScoreSnapshots AS Snapshot
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = Snapshot.ApplicationID;
    INSERT INTO @DeletedCounts VALUES (N'ApplicationScoreSnapshots', @@ROWCOUNT);

    DELETE History
    FROM dbo.ApplicationStatusHistory AS History
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = History.ApplicationID;
    INSERT INTO @DeletedCounts VALUES (N'ApplicationStatusHistory', @@ROWCOUNT);

    DELETE Letter
    FROM dbo.ReferenceLetters AS Letter
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = Letter.ApplicationID;
    INSERT INTO @DeletedCounts VALUES (N'ReferenceLetters', @@ROWCOUNT);

    DELETE Snapshot
    FROM dbo.ApplicationDocumentRequirementSnapshots AS Snapshot
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = Snapshot.ApplicationID;
    INSERT INTO @DeletedCounts VALUES (N'ApplicationDocumentRequirementSnapshots', @@ROWCOUNT);

    DELETE Application
    FROM dbo.Applications AS Application
    JOIN @TargetApplications AS TargetApplication ON TargetApplication.ApplicationID = Application.ApplicationID;
    INSERT INTO @DeletedCounts VALUES (N'Applications', @@ROWCOUNT);

    DELETE Criterion
    FROM dbo.ProgramOfferingEvaluationCriteria AS Criterion
    JOIN @TargetCriteria AS TargetCriterion ON TargetCriterion.CriterionID = Criterion.CriterionID;
    INSERT INTO @DeletedCounts VALUES (N'ProgramOfferingEvaluationCriteria', @@ROWCOUNT);

    DELETE Requirement
    FROM dbo.ProgramOfferingDocumentRequirements AS Requirement
    JOIN @TargetDocumentRequirements AS TargetRequirement
        ON TargetRequirement.RequirementID = Requirement.RequirementID;
    INSERT INTO @DeletedCounts VALUES (N'ProgramOfferingDocumentRequirements', @@ROWCOUNT);

    DELETE Requirement
    FROM dbo.ProgramOfferingExamRequirements AS Requirement
    JOIN @TargetOfferings AS TargetOffering
        ON TargetOffering.ProgramOfferingID = Requirement.ProgramOfferingID;
    INSERT INTO @DeletedCounts VALUES (N'ProgramOfferingExamRequirements', @@ROWCOUNT);

    DELETE Offering
    FROM dbo.ProgramOfferings AS Offering
    JOIN @TargetOfferings AS TargetOffering
        ON TargetOffering.ProgramOfferingID = Offering.ProgramOfferingID;
    INSERT INTO @DeletedCounts VALUES (N'ProgramOfferings', @@ROWCOUNT);

    DELETE Program
    FROM dbo.Programs AS Program
    JOIN @TargetPrograms AS TargetProgram ON TargetProgram.ProgramID = Program.ProgramID;
    INSERT INTO @DeletedCounts VALUES (N'Programs', @@ROWCOUNT);

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Programs
        WHERE ProgramName IN (N'smoke2', N'smokeprogram', N'smokes', N'UAT RC1 Program')
    )
    BEGIN
        THROW 52106, 'Hedef test programlarından biri silinemedi; transaction geri alınacak.', 1;
    END;

    COMMIT TRANSACTION;

    SELECT RecordType, RecordCount
    FROM @DeletedCounts
    ORDER BY RecordType;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
