/*
    DEVELOPMENT ONLY.
    Öğrenci test verilerini sıfırlar; katalog ve yönetici verilerini korur.
    Production admin arayüzünden çağrılmamalıdır.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DatabaseName sysname = DB_NAME();
IF @DatabaseName IS NULL
    OR (@DatabaseName NOT LIKE N'GraduateAppReset[_]%'
        AND @DatabaseName NOT LIKE N'GraduateAppTest[_]%'
        AND @DatabaseName NOT LIKE N'GraduateAppDev[_]%'
        AND @DatabaseName <> N'GraduateAppDevelopment')
    THROW 52000, 'Bu script yalnızca GraduateApp Development/Test veritabanlarında çalıştırılabilir.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @AdminCount bigint = (SELECT COUNT_BIG(*) FROM [dbo].[Admins]);
    DECLARE @InstituteCount bigint = (SELECT COUNT_BIG(*) FROM [dbo].[Institutes]);
    DECLARE @ProgramCount bigint = (SELECT COUNT_BIG(*) FROM [dbo].[Programs]);
    DECLARE @ExamCount bigint = (SELECT COUNT_BIG(*) FROM [dbo].[Exams]);
    DECLARE @OfferingCount bigint = (SELECT COUNT_BIG(*) FROM [dbo].[ProgramOfferings]);
    DECLARE @RequirementCount bigint = (SELECT COUNT_BIG(*) FROM [dbo].[ProgramOfferingExamRequirements]);

    DELETE FROM [dbo].[ApplicationScoreSnapshots];
    DELETE FROM [dbo].[ReferenceLetters];
    DELETE FROM [dbo].[ApplicationStatusHistory];
    DELETE FROM [dbo].[Applications];
    DELETE FROM [dbo].[StudentExamScores];
    DELETE FROM [dbo].[EducationInfo];
    DELETE FROM [dbo].[PasswordResetTokens] WHERE [TC] IS NOT NULL;
    DELETE FROM [dbo].[SystemLogs] WHERE [TC] IS NOT NULL;
    DELETE FROM [dbo].[SecurityAuditLogs] WHERE [TargetType] IN (N'Student', N'Application');
    DELETE FROM [dbo].[Students];

    IF @AdminCount <> (SELECT COUNT_BIG(*) FROM [dbo].[Admins])
        THROW 52001, 'Admins verisi değişti; işlem geri alındı.', 1;
    IF @InstituteCount <> (SELECT COUNT_BIG(*) FROM [dbo].[Institutes])
        THROW 52002, 'Institutes verisi değişti; işlem geri alındı.', 1;
    IF @ProgramCount <> (SELECT COUNT_BIG(*) FROM [dbo].[Programs])
        THROW 52003, 'Programs verisi değişti; işlem geri alındı.', 1;
    IF @ExamCount <> (SELECT COUNT_BIG(*) FROM [dbo].[Exams])
        THROW 52004, 'Exams verisi değişti; işlem geri alındı.', 1;
    IF @OfferingCount <> (SELECT COUNT_BIG(*) FROM [dbo].[ProgramOfferings])
        THROW 52005, 'ProgramOfferings verisi değişti; işlem geri alındı.', 1;
    IF @RequirementCount <> (SELECT COUNT_BIG(*) FROM [dbo].[ProgramOfferingExamRequirements])
        THROW 52006, 'ProgramOfferingExamRequirements verisi değişti; işlem geri alındı.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
