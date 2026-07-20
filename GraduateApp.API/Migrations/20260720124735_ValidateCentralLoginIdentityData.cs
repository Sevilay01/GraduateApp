using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GraduateApp.API.Migrations
{
    /// <inheritdoc />
    public partial class ValidateCentralLoginIdentityData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @affectedCount bigint;
                DECLARE @validationMessage nvarchar(2048);

                IF OBJECT_ID(N'[dbo].[LoginIdentities]', N'U') IS NULL
                BEGIN
                    THROW 51030, 'Merkezi giriş kimliği doğrulanamadı: LoginIdentities tablosu bulunamadı. Veri değiştirilmedi.', 1;
                END;

                SELECT @affectedCount =
                    (SELECT COUNT_BIG(*) FROM [dbo].[Students] WHERE [Email] IS NULL OR LTRIM(RTRIM([Email])) = N'')
                    + (SELECT COUNT_BIG(*) FROM [dbo].[Admins] WHERE [Email] IS NULL OR LTRIM(RTRIM([Email])) = N'');
                IF @affectedCount > 0
                BEGIN
                    SET @validationMessage = CONCAT(
                        N'Merkezi giriş kimliği doğrulanamadı: ',
                        @affectedCount,
                        N' hesapta e-posta null veya boş. Veri değiştirilmedi.');
                    THROW 51031, @validationMessage, 1;
                END;

                SELECT @affectedCount =
                    (SELECT COUNT_BIG(*) FROM [dbo].[Students] WHERE [NormalizedEmail] IS NULL OR LTRIM(RTRIM([NormalizedEmail])) = N'')
                    + (SELECT COUNT_BIG(*) FROM [dbo].[Admins] WHERE [NormalizedEmail] IS NULL OR LTRIM(RTRIM([NormalizedEmail])) = N'');
                IF @affectedCount > 0
                BEGIN
                    SET @validationMessage = CONCAT(
                        N'Merkezi giriş kimliği doğrulanamadı: ',
                        @affectedCount,
                        N' hesapta normalize e-posta null veya boş. Veri değiştirilmedi.');
                    THROW 51032, @validationMessage, 1;
                END;

                SELECT @affectedCount =
                    (SELECT COUNT_BIG(*)
                     FROM [dbo].[Students]
                     WHERE [NormalizedEmail] COLLATE Latin1_General_100_BIN2
                         <> UPPER(LTRIM(RTRIM([Email])) COLLATE Latin1_General_100_CI_AS) COLLATE Latin1_General_100_BIN2)
                    + (SELECT COUNT_BIG(*)
                       FROM [dbo].[Admins]
                       WHERE [NormalizedEmail] COLLATE Latin1_General_100_BIN2
                           <> UPPER(LTRIM(RTRIM([Email])) COLLATE Latin1_General_100_CI_AS) COLLATE Latin1_General_100_BIN2);
                IF @affectedCount > 0
                BEGIN
                    SET @validationMessage = CONCAT(
                        N'Merkezi giriş kimliği doğrulanamadı: ',
                        @affectedCount,
                        N' hesapta canonical normalize e-posta uyumsuz. Veri değiştirilmedi.');
                    THROW 51033, @validationMessage, 1;
                END;

                SELECT @affectedCount = COUNT_BIG(*)
                FROM [dbo].[Students] AS student
                INNER JOIN [dbo].[Admins] AS admin
                    ON UPPER(LTRIM(RTRIM(student.[Email])) COLLATE Latin1_General_100_CI_AS) COLLATE Latin1_General_100_BIN2
                       = UPPER(LTRIM(RTRIM(admin.[Email])) COLLATE Latin1_General_100_CI_AS) COLLATE Latin1_General_100_BIN2;
                IF @affectedCount > 0
                BEGIN
                    SET @validationMessage = CONCAT(
                        N'Merkezi giriş kimliği doğrulanamadı: öğrenci ve yönetici rolleri arasında ',
                        @affectedCount,
                        N' canonical e-posta çakışması var. Veri değiştirilmedi.');
                    THROW 51034, @validationMessage, 1;
                END;

                SELECT @affectedCount =
                    (SELECT COUNT_BIG(*)
                     FROM [dbo].[Students] AS student
                     WHERE 1 <> (
                         SELECT COUNT_BIG(*)
                         FROM [dbo].[LoginIdentities] AS identityInfo
                         WHERE identityInfo.[AccountType] = N'Student'
                           AND identityInfo.[StudentTC] = student.[TC]
                           AND identityInfo.[AdminID] IS NULL
                           AND identityInfo.[NormalizedEmail] COLLATE Latin1_General_100_BIN2
                               = student.[NormalizedEmail] COLLATE Latin1_General_100_BIN2))
                    + (SELECT COUNT_BIG(*)
                       FROM [dbo].[Admins] AS admin
                       WHERE 1 <> (
                           SELECT COUNT_BIG(*)
                           FROM [dbo].[LoginIdentities] AS identityInfo
                           WHERE identityInfo.[AccountType] = N'Admin'
                             AND identityInfo.[StudentTC] IS NULL
                             AND identityInfo.[AdminID] = admin.[AdminID]
                             AND identityInfo.[NormalizedEmail] COLLATE Latin1_General_100_BIN2
                                 = admin.[NormalizedEmail] COLLATE Latin1_General_100_BIN2));
                IF @affectedCount > 0
                BEGIN
                    SET @validationMessage = CONCAT(
                        N'Merkezi giriş kimliği doğrulanamadı: ',
                        @affectedCount,
                        N' hesap tam olarak bir doğru merkezi kimliğe dönüştürülemiyor. Veri değiştirilmedi.');
                    THROW 51035, @validationMessage, 1;
                END;

                SELECT @affectedCount = COUNT_BIG(*)
                FROM [dbo].[LoginIdentities] AS identityInfo
                WHERE CASE
                    WHEN identityInfo.[AccountType] = N'Student'
                         AND identityInfo.[StudentTC] IS NOT NULL
                         AND identityInfo.[AdminID] IS NULL
                         AND EXISTS (
                             SELECT 1
                             FROM [dbo].[Students] AS student
                             WHERE student.[TC] = identityInfo.[StudentTC]
                               AND student.[NormalizedEmail] COLLATE Latin1_General_100_BIN2
                                   = identityInfo.[NormalizedEmail] COLLATE Latin1_General_100_BIN2)
                        THEN 0
                    WHEN identityInfo.[AccountType] = N'Admin'
                         AND identityInfo.[StudentTC] IS NULL
                         AND identityInfo.[AdminID] IS NOT NULL
                         AND EXISTS (
                             SELECT 1
                             FROM [dbo].[Admins] AS admin
                             WHERE admin.[AdminID] = identityInfo.[AdminID]
                               AND admin.[NormalizedEmail] COLLATE Latin1_General_100_BIN2
                                   = identityInfo.[NormalizedEmail] COLLATE Latin1_General_100_BIN2)
                        THEN 0
                    ELSE 1
                END = 1;
                IF @affectedCount > 0
                BEGIN
                    SET @validationMessage = CONCAT(
                        N'Merkezi giriş kimliği doğrulanamadı: ',
                        @affectedCount,
                        N' merkezi kimlik kaydı rol veya subject ilişkisiyle uyumsuz. Veri değiştirilmedi.');
                    THROW 51036, @validationMessage, 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
