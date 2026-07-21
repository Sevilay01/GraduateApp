using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GraduateApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAccountLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[Admins]', N'U') IS NULL
                    THROW 51400, N'Yönetici hesap yaşam döngüsü migration ön kontrolü başarısız: Admins tablosu bulunamadı.', 1;

                IF OBJECT_ID(N'[dbo].[LoginIdentities]', N'U') IS NULL
                    THROW 51401, N'Yönetici hesap yaşam döngüsü migration ön kontrolü başarısız: LoginIdentities tablosu bulunamadı.', 1;

                IF OBJECT_ID(N'[dbo].[PasswordResetTokens]', N'U') IS NULL
                    THROW 51402, N'Yönetici hesap yaşam döngüsü migration ön kontrolü başarısız: PasswordResetTokens tablosu bulunamadı.', 1;

                IF COL_LENGTH(N'dbo.Admins', N'AdminID') IS NULL
                   OR COL_LENGTH(N'dbo.Admins', N'Email') IS NULL
                   OR COL_LENGTH(N'dbo.Admins', N'NormalizedEmail') IS NULL
                   OR COL_LENGTH(N'dbo.Admins', N'PasswordHash') IS NULL
                   OR COL_LENGTH(N'dbo.Admins', N'SecurityStamp') IS NULL
                   OR COL_LENGTH(N'dbo.LoginIdentities', N'LoginIdentityID') IS NULL
                   OR COL_LENGTH(N'dbo.LoginIdentities', N'NormalizedEmail') IS NULL
                   OR COL_LENGTH(N'dbo.LoginIdentities', N'AccountType') IS NULL
                   OR COL_LENGTH(N'dbo.LoginIdentities', N'StudentTC') IS NULL
                   OR COL_LENGTH(N'dbo.LoginIdentities', N'AdminID') IS NULL
                   OR COL_LENGTH(N'dbo.PasswordResetTokens', N'TokenID') IS NULL
                   OR COL_LENGTH(N'dbo.PasswordResetTokens', N'AdminID') IS NULL
                   OR COL_LENGTH(N'dbo.PasswordResetTokens', N'TokenHash') IS NULL
                    THROW 51403, N'Yönetici hesap yaşam döngüsü migration ön kontrolü başarısız: beklenen kolonlardan biri bulunamadı.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [dbo].[Admins]
                    GROUP BY [NormalizedEmail]
                    HAVING COUNT_BIG(*) > 1
                )
                    THROW 51404, N'Yönetici hesap yaşam döngüsü migration ön kontrolü başarısız: aynı normalize e-postaya sahip birden fazla yönetici bulundu. Veri otomatik birleştirilmedi.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [dbo].[LoginIdentities]
                    GROUP BY [NormalizedEmail]
                    HAVING COUNT_BIG(*) > 1
                )
                    THROW 51409, N'Yönetici hesap yaşam döngüsü migration ön kontrolü başarısız: aynı normalize e-postaya sahip birden fazla merkezi kimlik bulundu. Veri otomatik birleştirilmedi.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [dbo].[LoginIdentities]
                    WHERE [AdminID] IS NOT NULL
                    GROUP BY [AdminID]
                    HAVING COUNT_BIG(*) > 1
                )
                    THROW 51405, N'Yönetici hesap yaşam döngüsü migration ön kontrolü başarısız: bir yönetici birden fazla merkezi kimliğe bağlı. Veri otomatik değiştirilmedi.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [dbo].[Admins] AS [admin]
                    LEFT JOIN [dbo].[LoginIdentities] AS [identity]
                        ON [identity].[AdminID] = [admin].[AdminID]
                    WHERE [identity].[LoginIdentityID] IS NULL
                       OR [identity].[AccountType] <> N'Admin'
                       OR [identity].[StudentTC] IS NOT NULL
                       OR [identity].[NormalizedEmail] <> [admin].[NormalizedEmail]
                )
                    THROW 51406, N'Yönetici hesap yaşam döngüsü migration ön kontrolü başarısız: yönetici ile merkezi kimliği arasında tutarsızlık bulundu. Veri otomatik değiştirilmedi.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [dbo].[LoginIdentities] AS [identity]
                    LEFT JOIN [dbo].[Admins] AS [admin]
                        ON [admin].[AdminID] = [identity].[AdminID]
                    WHERE [identity].[AccountType] = N'Admin'
                      AND ([identity].[AdminID] IS NULL OR [admin].[AdminID] IS NULL)
                )
                    THROW 51407, N'Yönetici hesap yaşam döngüsü migration ön kontrolü başarısız: yetim yönetici LoginIdentity kaydı bulundu. Veri otomatik değiştirilmedi.', 1;
                """);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "PasswordResetTokens",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "PasswordReset");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Admins",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInvitationPending",
                table: "Admins",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PublicID",
                table: "Admins",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [dbo].[Admins]
                SET [PublicID] = NEWID()
                WHERE [PublicID] IS NULL;

                IF EXISTS (SELECT 1 FROM [dbo].[Admins] WHERE [PublicID] IS NULL)
                    THROW 51408, N'Yönetici PublicID alanı güvenli biçimde doldurulamadı.', 1;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "PublicID",
                table: "Admins",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Admins",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PasswordResetTokens_Purpose",
                table: "PasswordResetTokens",
                sql: "[Purpose] IN (N'PasswordReset', N'AdminInvitation') AND ([Purpose] <> N'AdminInvitation' OR [AdminID] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_Admins_PublicID",
                table: "Admins",
                column: "PublicID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "AddAdminAccountLifecycle migration geri alınamaz; yönetici yaşam döngüsü ve token amacı alanlarını kaldırmak veri kaybına yol açabilir.");
        }
    }
}
