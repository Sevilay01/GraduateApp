using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Models;

public sealed class GraduateAppDbContext(DbContextOptions<GraduateAppDbContext> options) : DbContext(options)
{
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationDocument> ApplicationDocuments => Set<ApplicationDocument>();
    public DbSet<ApplicationDocumentRequirementSnapshot> ApplicationDocumentRequirementSnapshots => Set<ApplicationDocumentRequirementSnapshot>();
    public DbSet<ApplicationScoreSnapshot> ApplicationScoreSnapshots => Set<ApplicationScoreSnapshot>();
    public DbSet<ApplicationStatusHistory> ApplicationStatusHistories => Set<ApplicationStatusHistory>();
    public DbSet<EducationInfo> EducationInfos => Set<EducationInfo>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<Institute> Institutes => Set<Institute>();
    public DbSet<LoginIdentity> LoginIdentities => Set<LoginIdentity>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Program> Programs => Set<Program>();
    public DbSet<ProgramOffering> ProgramOfferings => Set<ProgramOffering>();
    public DbSet<ProgramOfferingExamRequirement> ProgramOfferingExamRequirements => Set<ProgramOfferingExamRequirement>();
    public DbSet<ProgramOfferingDocumentRequirement> ProgramOfferingDocumentRequirements => Set<ProgramOfferingDocumentRequirement>();
    public DbSet<ReferenceLetter> ReferenceLetters => Set<ReferenceLetter>();
    public DbSet<SecurityAuditLog> SecurityAuditLogs => Set<SecurityAuditLog>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<StudentExamScore> StudentExamScores => Set<StudentExamScore>();
    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();
    public DbSet<University> Universities => Set<University>();
    public DbSet<VwAdminApplicationSummary> VwAdminApplicationSummaries => Set<VwAdminApplicationSummary>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Admin>(entity =>
        {
            entity.HasKey(e => e.AdminId);
            entity.HasIndex(e => e.PublicId).IsUnique();
            entity.HasIndex(e => e.NormalizedEmail).IsUnique();
            entity.Property(e => e.AdminId).HasColumnName("AdminID");
            entity.Property(e => e.PublicId).HasColumnName("PublicID");
            entity.Property(e => e.Email).HasMaxLength(254).IsRequired();
            entity.Property(e => e.NormalizedEmail).HasMaxLength(254).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(512).IsUnicode(false).IsRequired();
            entity.Property(e => e.SecurityStamp).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(e => e.LockoutEndUtc).HasColumnType("datetimeoffset");
            entity.Property(e => e.CreatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.UpdatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.IsInvitationPending).HasDefaultValue(false);
            entity.Property(e => e.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasKey(e => e.ApplicationId);
            entity.HasIndex(e => e.PublicId).IsUnique();
            entity.HasIndex(e => new { e.Tc, e.ProgramOfferingId }).IsUnique();
            entity.ToTable("Applications", table => table.HasCheckConstraint(
                "CK_Applications_CurrentStatus",
                "[CurrentStatus] IN (N'Draft',N'Pending',N'UnderReview',N'Approved',N'Rejected',N'Withdrawn')"));
            entity.Property(e => e.ApplicationId).HasColumnName("ApplicationID");
            entity.Property(e => e.PublicId).HasColumnName("PublicID").HasDefaultValueSql("NEWID()");
            entity.Property(e => e.ApplicationDate).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.CurrentStatus).HasMaxLength(50).HasDefaultValue("Pending").IsRequired();
            entity.Property(e => e.UsesDocumentWorkflow).HasDefaultValue(false);
            entity.Property(e => e.ProgramOfferingId).HasColumnName("ProgramOfferingID");
            entity.Property(e => e.Tc).HasMaxLength(11).IsUnicode(false).IsFixedLength().HasColumnName("TC").IsRequired();
            entity.Property(e => e.RowVersion).IsRowVersion();

            entity.HasOne(e => e.ProgramOffering).WithMany(e => e.Applications)
                .HasForeignKey(e => e.ProgramOfferingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.TcNavigation).WithMany(e => e.Applications)
                .HasForeignKey(e => e.Tc).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationDocumentRequirementSnapshot>(entity =>
        {
            entity.HasKey(e => e.SnapshotId);
            entity.HasIndex(e => e.PublicId).IsUnique();
            entity.HasIndex(e => new { e.ApplicationId, e.DocumentCode }).IsUnique();
            entity.Property(e => e.SnapshotId).HasColumnName("SnapshotID");
            entity.Property(e => e.PublicId).HasColumnName("PublicID").HasDefaultValueSql("NEWID()");
            entity.Property(e => e.ApplicationId).HasColumnName("ApplicationID");
            entity.Property(e => e.SourceRequirementId).HasColumnName("SourceRequirementID");
            entity.Property(e => e.DocumentCode).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(150).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.AllowedContentCategory).HasConversion<string>().HasMaxLength(32).IsUnicode(false).IsRequired();
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.ToTable("ApplicationDocumentRequirementSnapshots", table =>
            {
                table.HasCheckConstraint("CK_ApplicationDocumentRequirementSnapshots_MaximumBytes", "[MaximumBytes] BETWEEN 1 AND 104857600");
                table.HasCheckConstraint("CK_ApplicationDocumentRequirementSnapshots_ContentCategory", "[AllowedContentCategory] IN ('PdfOnly','ImageOnly','PdfOrImage')");
            });
            entity.HasOne(e => e.Application).WithMany(e => e.DocumentRequirementSnapshots)
                .HasForeignKey(e => e.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SourceRequirement).WithMany(e => e.Snapshots)
                .HasForeignKey(e => e.SourceRequirementId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationDocument>(entity =>
        {
            entity.HasKey(e => e.DocumentId);
            entity.HasIndex(e => e.PublicId).IsUnique();
            entity.HasIndex(e => new { e.RequirementSnapshotId, e.VersionNumber }).IsUnique();
            entity.HasIndex(e => e.ApplicationId);
            entity.HasIndex(e => e.RequirementSnapshotId).IsUnique().HasFilter("[IsCurrent] = CAST(1 AS bit)");
            entity.HasIndex(e => e.ObjectKey).IsUnique();
            entity.Property(e => e.DocumentId).HasColumnName("DocumentID");
            entity.Property(e => e.PublicId).HasColumnName("PublicID").HasDefaultValueSql("NEWID()");
            entity.Property(e => e.ApplicationId).HasColumnName("ApplicationID");
            entity.Property(e => e.RequirementSnapshotId).HasColumnName("RequirementSnapshotID");
            entity.Property(e => e.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ObjectKey).HasMaxLength(80).IsUnicode(false).IsRequired();
            entity.Property(e => e.VerifiedContentType).HasMaxLength(50).IsUnicode(false).IsRequired();
            entity.Property(e => e.Sha256).HasMaxLength(64).IsUnicode(false).IsFixedLength().IsRequired();
            entity.Property(e => e.ReviewStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.Property(e => e.UploadedAtUtc).HasColumnType("datetime2");
            entity.Property(e => e.ReviewedAtUtc).HasColumnType("datetime2");
            entity.Property(e => e.ReviewedByAdminId).HasColumnName("ReviewedByAdminID");
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.ToTable("ApplicationDocuments", table =>
            {
                table.HasCheckConstraint("CK_ApplicationDocuments_VersionNumber", "[VersionNumber] > 0");
                table.HasCheckConstraint("CK_ApplicationDocuments_FileSize", "[FileSize] > 0");
                table.HasCheckConstraint("CK_ApplicationDocuments_Review", "([ReviewStatus] = 'Rejected' AND [RejectionReason] IS NOT NULL AND LEN(LTRIM(RTRIM([RejectionReason]))) > 0) OR ([ReviewStatus] IN ('Pending','Approved') AND [RejectionReason] IS NULL)");
                table.HasCheckConstraint("CK_ApplicationDocuments_ContentType", "[VerifiedContentType] IN ('application/pdf','image/jpeg','image/png')");
                table.HasCheckConstraint("CK_ApplicationDocuments_Sha256", "LEN([Sha256]) = 64 AND [Sha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                table.HasCheckConstraint("CK_ApplicationDocuments_ObjectKey", "LEN([ObjectKey]) = 32 AND [ObjectKey] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
            });
            entity.HasOne(e => e.Application).WithMany(e => e.Documents)
                .HasForeignKey(e => e.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.RequirementSnapshot).WithMany(e => e.Documents)
                .HasForeignKey(e => e.RequirementSnapshotId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ReviewedByAdmin).WithMany(e => e.ReviewedApplicationDocuments)
                .HasForeignKey(e => e.ReviewedByAdminId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationScoreSnapshot>(entity =>
        {
            entity.HasKey(e => e.ApplicationScoreSnapshotId);
            entity.HasIndex(e => new { e.ApplicationId, e.ExamId }).IsUnique();
            entity.Property(e => e.ApplicationScoreSnapshotId).HasColumnName("ApplicationScoreSnapshotID");
            entity.Property(e => e.ApplicationId).HasColumnName("ApplicationID");
            entity.Property(e => e.ExamId).HasColumnName("ExamID");
            entity.Property(e => e.ExamNameSnapshot).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ScoreSnapshot).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.ExamDateSnapshot).HasColumnType("date");
            entity.Property(e => e.CapturedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne(e => e.Application).WithMany(e => e.ScoreSnapshots)
                .HasForeignKey(e => e.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Exam).WithMany(e => e.ApplicationScoreSnapshots)
                .HasForeignKey(e => e.ExamId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationStatusHistory>(entity =>
        {
            entity.HasKey(e => e.HistoryId);
            entity.ToTable("ApplicationStatusHistory");
            entity.Property(e => e.HistoryId).HasColumnName("HistoryID");
            entity.Property(e => e.ApplicationId).HasColumnName("ApplicationID");
            entity.Property(e => e.PreviousStatus).HasMaxLength(50);
            entity.Property(e => e.StatusName).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ChangedByAdminId).HasColumnName("ChangedByAdminID");
            entity.Property(e => e.ChangeDate).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.HasOne(e => e.Application).WithMany(e => e.ApplicationStatusHistories)
                .HasForeignKey(e => e.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ChangedByAdmin).WithMany(e => e.ApplicationStatusHistories)
                .HasForeignKey(e => e.ChangedByAdminId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EducationInfo>(entity =>
        {
            entity.HasKey(e => e.EducationId);
            entity.ToTable("EducationInfo");
            entity.Property(e => e.EducationId).HasColumnName("EducationID");
            entity.Property(e => e.Tc).HasMaxLength(11).IsUnicode(false).IsFixedLength().HasColumnName("TC").IsRequired();
            entity.Property(e => e.UniversityId).HasColumnName("UniversityID");
            entity.Property(e => e.Faculty).HasMaxLength(100);
            entity.Property(e => e.GraduatedProgram).HasMaxLength(100);
            entity.Property(e => e.Gno).HasColumnType("decimal(3, 2)").HasColumnName("GNO");
            entity.Property(e => e.DiplomaPath).HasMaxLength(255);
            entity.Property(e => e.TranscriptPath).HasMaxLength(255);
            entity.HasOne(e => e.TcNavigation).WithMany(e => e.EducationInfos)
                .HasForeignKey(e => e.Tc).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.University).WithMany(e => e.EducationInfos)
                .HasForeignKey(e => e.UniversityId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Exam>(entity =>
        {
            entity.HasKey(e => e.ExamId);
            entity.HasIndex(e => e.ExamName).IsUnique();
            entity.Property(e => e.ExamId).HasColumnName("ExamID");
            entity.Property(e => e.ExamName).HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<Institute>(entity =>
        {
            entity.HasKey(e => e.InstituteId);
            entity.HasIndex(e => e.InstituteName).IsUnique();
            entity.ToTable("Institutes", table => table.HasCheckConstraint(
                "CK_Institutes_InstituteName_Trimmed",
                "[InstituteName] = LTRIM(RTRIM([InstituteName])) AND LEN([InstituteName]) >= 2"));
            entity.Property(e => e.InstituteId).HasColumnName("InstituteID");
            entity.Property(e => e.InstituteName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.UpdatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<LoginIdentity>(entity =>
        {
            entity.HasKey(e => e.LoginIdentityId);
            entity.HasIndex(e => e.NormalizedEmail).IsUnique();
            entity.HasIndex(e => e.StudentTc).IsUnique().HasFilter("[StudentTC] IS NOT NULL");
            entity.HasIndex(e => e.AdminId).IsUnique().HasFilter("[AdminID] IS NOT NULL");
            entity.ToTable("LoginIdentities", table => table.HasCheckConstraint(
                "CK_LoginIdentities_Subject",
                "([AccountType] = N'Student' AND [StudentTC] IS NOT NULL AND [AdminID] IS NULL) OR ([AccountType] = N'Admin' AND [StudentTC] IS NULL AND [AdminID] IS NOT NULL)"));
            entity.Property(e => e.LoginIdentityId).HasColumnName("LoginIdentityID");
            entity.Property(e => e.NormalizedEmail).HasMaxLength(254).IsRequired();
            entity.Property(e => e.AccountType).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.StudentTc).HasMaxLength(11).IsUnicode(false).IsFixedLength().HasColumnName("StudentTC");
            entity.Property(e => e.AdminId).HasColumnName("AdminID");
            entity.Property(e => e.CreatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.HasOne(e => e.Student).WithOne(e => e.LoginIdentity)
                .HasForeignKey<LoginIdentity>(e => e.StudentTc).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Admin).WithOne(e => e.LoginIdentity)
                .HasForeignKey<LoginIdentity>(e => e.AdminId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.TokenId);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.ToTable("PasswordResetTokens", table =>
            {
                table.HasCheckConstraint(
                    "CK_PasswordResetTokens_Subject",
                    "([TC] IS NOT NULL AND [AdminID] IS NULL) OR ([TC] IS NULL AND [AdminID] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_PasswordResetTokens_Purpose",
                    "[Purpose] IN (N'PasswordReset', N'AdminInvitation') AND ([Purpose] <> N'AdminInvitation' OR [AdminID] IS NOT NULL)");
            });
            entity.Property(e => e.TokenId).HasColumnName("TokenID");
            entity.Property(e => e.Tc).HasMaxLength(11).IsUnicode(false).IsFixedLength().HasColumnName("TC");
            entity.Property(e => e.AdminId).HasColumnName("AdminID");
            entity.Property(e => e.TokenHash).HasMaxLength(256).IsUnicode(false).IsRequired();
            entity.Property(e => e.Purpose)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(PasswordResetTokenPurpose.PasswordReset)
                .HasSentinel(default(PasswordResetTokenPurpose))
                .IsRequired();
            entity.Property(e => e.ExpirationDate).HasColumnType("datetime2");
            entity.Property(e => e.IsUsed).HasDefaultValue(false);
            entity.Property(e => e.CreatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.HasOne(e => e.TcNavigation).WithMany(e => e.PasswordResetTokens)
                .HasForeignKey(e => e.Tc).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Admin).WithMany(e => e.PasswordResetTokens)
                .HasForeignKey(e => e.AdminId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Program>(entity =>
        {
            entity.HasKey(e => e.ProgramId);
            entity.HasIndex(e => new { e.InstituteId, e.ProgramName, e.DegreeType }).IsUnique();
            entity.ToTable("Programs", table =>
            {
                table.HasCheckConstraint(
                    "CK_Programs_ProgramName_Trimmed",
                    "[ProgramName] = LTRIM(RTRIM([ProgramName])) AND LEN([ProgramName]) >= 2");
                table.HasCheckConstraint(
                    "CK_Programs_DegreeType",
                    "[DegreeType] IN (N'Doktora',N'Tezli Yüksek Lisans',N'Tezsiz Yüksek Lisans',N'Uzaktan Tezsiz Yüksek Lisans')");
            });
            entity.Property(e => e.ProgramId).HasColumnName("ProgramID");
            entity.Property(e => e.InstituteId).HasColumnName("InstituteID");
            entity.Property(e => e.ProgramName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.DegreeType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.UpdatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.HasOne(e => e.Institute).WithMany(e => e.Programs)
                .HasForeignKey(e => e.InstituteId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProgramOffering>(entity =>
        {
            entity.HasKey(e => e.ProgramOfferingId);
            entity.HasIndex(e => new { e.ProgramId, e.AcademicYearStart, e.Term }).IsUnique();
            entity.ToTable("ProgramOfferings", table =>
            {
                table.HasCheckConstraint(
                    "CK_ProgramOfferings_AcademicPeriod",
                    "([AcademicYearStart] = 0 AND [Term] = 0) OR ([AcademicYearStart] BETWEEN 2000 AND 2200 AND [Term] IN (1,2,3))");
                table.HasCheckConstraint("CK_ProgramOfferings_Quota", "[Quota] >= 0");
                table.HasCheckConstraint(
                    "CK_ProgramOfferings_DateRange",
                    "[ApplicationStartUtc] IS NULL OR [ApplicationDeadlineUtc] IS NULL OR [ApplicationStartUtc] < [ApplicationDeadlineUtc]");
            });
            entity.Property(e => e.ProgramOfferingId).HasColumnName("ProgramOfferingID");
            entity.Property(e => e.ProgramId).HasColumnName("ProgramID");
            entity.Property(e => e.Term).HasConversion<int>();
            entity.Property(e => e.ApplicationStartUtc).HasColumnType("datetime2");
            entity.Property(e => e.ApplicationDeadlineUtc).HasColumnType("datetime2");
            entity.Property(e => e.CreatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.UpdatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.HasOne(e => e.Program).WithMany(e => e.Offerings)
                .HasForeignKey(e => e.ProgramId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProgramOfferingExamRequirement>(entity =>
        {
            entity.HasKey(e => e.RequirementId);
            entity.HasIndex(e => new { e.ProgramOfferingId, e.ExamId }).IsUnique();
            entity.ToTable("ProgramOfferingExamRequirements", table =>
                table.HasCheckConstraint("CK_ProgramOfferingExamRequirements_MinimumScore", "[MinimumScore] >= 0"));
            entity.Property(e => e.RequirementId).HasColumnName("RequirementID");
            entity.Property(e => e.ProgramOfferingId).HasColumnName("ProgramOfferingID");
            entity.Property(e => e.ExamId).HasColumnName("ExamID");
            entity.Property(e => e.MinimumScore).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.MinimumValidityDate).HasColumnType("date");
            entity.HasOne(e => e.ProgramOffering).WithMany(e => e.ExamRequirements)
                .HasForeignKey(e => e.ProgramOfferingId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Exam).WithMany(e => e.ProgramOfferingExamRequirements)
                .HasForeignKey(e => e.ExamId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProgramOfferingDocumentRequirement>(entity =>
        {
            entity.HasKey(e => e.RequirementId);
            entity.HasIndex(e => e.PublicId).IsUnique();
            entity.HasIndex(e => new { e.ProgramOfferingId, e.NormalizedDocumentCode }).IsUnique();
            entity.Property(e => e.RequirementId).HasColumnName("RequirementID");
            entity.Property(e => e.PublicId).HasColumnName("PublicID").HasDefaultValueSql("NEWID()");
            entity.Property(e => e.ProgramOfferingId).HasColumnName("ProgramOfferingID");
            entity.Property(e => e.DocumentCode).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(e => e.NormalizedDocumentCode).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(150).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.AllowedContentCategory).HasConversion<string>().HasMaxLength(32).IsUnicode(false).IsRequired();
            entity.Property(e => e.CreatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.UpdatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.ToTable("ProgramOfferingDocumentRequirements", table =>
            {
                table.HasCheckConstraint("CK_ProgramOfferingDocumentRequirements_MaximumBytes", "[MaximumBytes] BETWEEN 1 AND 104857600");
                table.HasCheckConstraint("CK_ProgramOfferingDocumentRequirements_ContentCategory", "[AllowedContentCategory] IN ('PdfOnly','ImageOnly','PdfOrImage')");
            });
            entity.HasOne(e => e.ProgramOffering).WithMany(e => e.DocumentRequirements)
                .HasForeignKey(e => e.ProgramOfferingId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReferenceLetter>(entity =>
        {
            entity.HasKey(e => e.ReferenceId);
            entity.Property(e => e.ReferenceId).HasColumnName("ReferenceID");
            entity.Property(e => e.ApplicationId).HasColumnName("ApplicationID");
            entity.Property(e => e.AcademicianName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.AcademicianEmail).HasMaxLength(254).IsRequired();
            entity.Property(e => e.FilePath).HasMaxLength(255).IsRequired();
            entity.HasOne(e => e.Application).WithMany(e => e.ReferenceLetters)
                .HasForeignKey(e => e.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SecurityAuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditId);
            entity.Property(e => e.ActorAdminId).HasColumnName("ActorAdminID");
            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.TargetType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.TargetId).HasMaxLength(100).IsUnicode(false).IsRequired();
            entity.Property(e => e.Details).HasMaxLength(1000);
            entity.Property(e => e.CreatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(e => e.CreatedAtUtc);
        });

        modelBuilder.Entity<Student>(entity =>
        {
            entity.HasKey(e => e.Tc);
            entity.HasIndex(e => e.PublicId).IsUnique();
            entity.HasIndex(e => e.NormalizedEmail).IsUnique();
            entity.HasIndex(e => e.Telephone).IsUnique().HasFilter("[Telephone] IS NOT NULL");
            entity.Property(e => e.Tc).HasMaxLength(11).IsUnicode(false).IsFixedLength().HasColumnName("TC");
            entity.Property(e => e.PublicId).HasColumnName("PublicID").HasDefaultValueSql("NEWID()");
            entity.Property(e => e.StudentName).HasMaxLength(50).IsRequired();
            entity.Property(e => e.StudentSurname).HasMaxLength(50).IsRequired();
            entity.Property(e => e.FatherName).HasMaxLength(50);
            entity.Property(e => e.BirthDate).HasColumnType("date");
            entity.Property(e => e.Email).HasMaxLength(254).IsRequired();
            entity.Property(e => e.NormalizedEmail).HasMaxLength(254).IsRequired();
            entity.Property(e => e.Telephone).HasMaxLength(15).IsUnicode(false);
            entity.Property(e => e.PasswordHash).HasMaxLength(512).IsUnicode(false).IsRequired();
            entity.Property(e => e.SecurityStamp).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LockoutEndUtc).HasColumnType("datetimeoffset");
            entity.Property(e => e.CreatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.UpdatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<StudentExamScore>(entity =>
        {
            entity.HasKey(e => e.ScoreId);
            entity.HasIndex(e => new { e.Tc, e.ExamId }).IsUnique();
            entity.Property(e => e.ScoreId).HasColumnName("ScoreID");
            entity.Property(e => e.ExamId).HasColumnName("ExamID");
            entity.Property(e => e.Tc).HasMaxLength(11).IsUnicode(false).IsFixedLength().HasColumnName("TC");
            entity.Property(e => e.Score).HasColumnType("decimal(5, 2)");
            entity.HasOne(e => e.Exam).WithMany(e => e.StudentExamScores)
                .HasForeignKey(e => e.ExamId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.TcNavigation).WithMany(e => e.StudentExamScores)
                .HasForeignKey(e => e.Tc).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SystemLog>(entity =>
        {
            entity.HasKey(e => e.LogId);
            entity.Property(e => e.LogId).HasColumnName("LogID");
            entity.Property(e => e.AdminId).HasColumnName("AdminID");
            entity.Property(e => e.Tc).HasMaxLength(11).IsUnicode(false).IsFixedLength().HasColumnName("TC");
            entity.Property(e => e.ActionType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.LogDetails).HasColumnType("xml");
            entity.Property(e => e.LogDate).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne(e => e.Admin).WithMany(e => e.SystemLogs)
                .HasForeignKey(e => e.AdminId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.TcNavigation).WithMany(e => e.SystemLogs)
                .HasForeignKey(e => e.Tc).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<University>(entity =>
        {
            entity.HasKey(e => e.UniversityId);
            entity.HasIndex(e => e.UniversityName).IsUnique();
            entity.Property(e => e.UniversityId).HasColumnName("UniversityID");
            entity.Property(e => e.UniversityName).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<VwAdminApplicationSummary>(entity =>
        {
            entity.HasNoKey().ToView("vw_AdminApplicationSummary");
            entity.Property(e => e.AdSoyad).HasMaxLength(101);
            entity.Property(e => e.BasvuruTarihi).HasColumnType("datetime");
            entity.Property(e => e.Enstitu).HasMaxLength(100);
            entity.Property(e => e.Eposta).HasMaxLength(100);
            entity.Property(e => e.GuncelDurum).HasMaxLength(50);
            entity.Property(e => e.KimlikNo).HasMaxLength(11).IsUnicode(false).IsFixedLength();
            entity.Property(e => e.LisansOrtalamasi).HasColumnType("decimal(3, 2)");
            entity.Property(e => e.MezunOlduguUniversite).HasMaxLength(100);
            entity.Property(e => e.Program).HasMaxLength(100);
        });
    }
}
