BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260720082324_AddStudentActivationAndPublicId'
)
BEGIN
    ALTER TABLE [Students] ADD [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260720082324_AddStudentActivationAndPublicId'
)
BEGIN
    ALTER TABLE [Students] ADD [PublicID] uniqueidentifier NOT NULL DEFAULT (NEWID());
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260720082324_AddStudentActivationAndPublicId'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Students_PublicID] ON [Students] ([PublicID]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260720082324_AddStudentActivationAndPublicId'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260720082324_AddStudentActivationAndPublicId', N'10.0.10');
END;

COMMIT;
GO

