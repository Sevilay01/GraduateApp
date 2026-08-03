using GraduateApp.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Models;

public sealed class GraduateAppDbContext(DbContextOptions<GraduateAppDbContext> options) : DbContext(options)
{
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationDocument> ApplicationDocuments => Set<ApplicationDocument>();
    public DbSet<ApplicationDocumentRequirementSnapshot> ApplicationDocumentRequirementSnapshots => Set<ApplicationDocumentRequirementSnapshot>();
    public DbSet<ApplicationEvaluation> ApplicationEvaluations => Set<ApplicationEvaluation>();
    public DbSet<ApplicationEvaluationComponent> ApplicationEvaluationComponents => Set<ApplicationEvaluationComponent>();
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
    public DbSet<ProgramOfferingEvaluationCriterion> ProgramOfferingEvaluationCriteria => Set<ProgramOfferingEvaluationCriterion>();
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
            entity.Property(e => e.UsesEvaluationWorkflow).HasDefaultValue(false);
            entity.Property(e => e.ProgramOfferingId).HasColumnName("ProgramOfferingID");
            entity.Property(e => e.Tc).HasMaxLength(11).IsUnicode(false).IsFixedLength().HasColumnName("TC").IsRequired();
            entity.Property(e => e.RowVersion).IsRowVersion();

            entity.HasOne(e => e.ProgramOffering).WithMany(e => e.Applications)
                .HasForeignKey(e => e.ProgramOfferingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.TcNavigation).WithMany(e => e.Applications)
                .HasForeignKey(e => e.Tc).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationEvaluation>(entity =>
        {
            entity.HasKey(e => e.ApplicationEvaluationId);
            entity.HasIndex(e => e.ApplicationId).IsUnique();
            entity.HasIndex(e => new { e.ProgramOfferingId, e.Rank }).IsUnique().HasFilter("[Rank] IS NOT NULL");
            entity.Property(e => e.ApplicationEvaluationId).HasColumnName("ApplicationEvaluationID");
            entity.Property(e => e.ApplicationId).HasColumnName("ApplicationID");
            entity.Property(e => e.ProgramOfferingId).HasColumnName("ProgramOfferingID");
            entity.Property(e => e.EligibilityStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
            entity.Property(e => e.IneligibilityReason).HasMaxLength(500);
            entity.Property(e => e.TotalScore).HasColumnType("decimal(7, 4)");
            entity.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.EligibilityDecidedByAdminId).HasColumnName("EligibilityDecidedByAdminID");
            entity.Property(e => e.EligibilityDecidedAtUtc).HasColumnType("datetime2");
            entity.Property(e => e.FinalizedAtUtc).HasColumnType("datetime2");
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.ToTable("ApplicationEvaluations", table =>
            {
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluations_EligibilityStatus",
                    "[EligibilityStatus] IN ('Pending','Eligible','Ineligible')");
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluations_Outcome",
                    "[Outcome] IS NULL OR [Outcome] IN ('Admitted','NotAdmitted','Ineligible')");
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluations_TotalScore",
                    "[TotalScore] IS NULL OR [TotalScore] BETWEEN 0 AND 100");
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluations_Rank",
                    "[Rank] IS NULL OR [Rank] > 0");
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluations_IneligibilityReason",
                    "([EligibilityStatus] = 'Ineligible' AND [IneligibilityReason] IS NOT NULL AND LEN(LTRIM(RTRIM([IneligibilityReason]))) > 0) OR ([EligibilityStatus] IN ('Pending','Eligible') AND [IneligibilityReason] IS NULL)");
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluations_EligibilityDecision",
                    "([EligibilityStatus] = 'Pending' AND [EligibilityDecidedByAdminID] IS NULL AND [EligibilityDecidedAtUtc] IS NULL) OR ([EligibilityStatus] IN ('Eligible','Ineligible') AND [EligibilityDecidedByAdminID] IS NOT NULL AND [EligibilityDecidedAtUtc] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluations_FinalResult",
                    "([Outcome] IS NULL AND [Rank] IS NULL AND [FinalizedAtUtc] IS NULL) OR ([Outcome] IN ('Admitted','NotAdmitted') AND [Rank] IS NOT NULL AND [TotalScore] IS NOT NULL AND [FinalizedAtUtc] IS NOT NULL) OR ([Outcome] = 'Ineligible' AND [Rank] IS NULL AND [TotalScore] IS NULL AND [FinalizedAtUtc] IS NOT NULL)");
            });
            entity.HasOne(e => e.Application).WithOne(e => e.Evaluation)
                .HasForeignKey<ApplicationEvaluation>(e => e.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ProgramOffering).WithMany()
                .HasForeignKey(e => e.ProgramOfferingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.EligibilityDecidedByAdmin).WithMany()
                .HasForeignKey(e => e.EligibilityDecidedByAdminId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationEvaluationComponent>(entity =>
        {
            entity.HasKey(e => e.ComponentId);
            entity.HasIndex(e => new { e.ApplicationEvaluationId, e.CodeSnapshot }).IsUnique();
            entity.HasIndex(e => new { e.ApplicationEvaluationId, e.TieBreakPrioritySnapshot }).IsUnique();
            entity.Property(e => e.ComponentId).HasColumnName("ComponentID");
            entity.Property(e => e.ApplicationEvaluationId).HasColumnName("ApplicationEvaluationID");
            entity.Property(e => e.SourceCriterionId).HasColumnName("SourceCriterionID");
            entity.Property(e => e.CriterionPublicIdSnapshot).HasColumnName("CriterionPublicIDSnapshot");
            entity.Property(e => e.CodeSnapshot).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(e => e.DisplayNameSnapshot).HasMaxLength(150).IsRequired();
            entity.Property(e => e.SourceTypeSnapshot).HasConversion<string>().HasMaxLength(32).IsUnicode(false).IsRequired();
            entity.Property(e => e.ExamId).HasColumnName("ExamID");
            entity.Property(e => e.RawScore).HasColumnType("decimal(9, 4)");
            entity.Property(e => e.MaximumRawScoreSnapshot).HasColumnType("decimal(9, 4)");
            entity.Property(e => e.NormalizedScore).HasColumnType("decimal(7, 4)");
            entity.Property(e => e.WeightedScore).HasColumnType("decimal(7, 4)");
            entity.Property(e => e.ManualScoredByAdminId).HasColumnName("ManualScoredByAdminID");
            entity.Property(e => e.ManualScoredAtUtc).HasColumnType("datetime2");
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.ToTable("ApplicationEvaluationComponents", table =>
            {
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluationComponents_SourceType",
                    "[SourceTypeSnapshot] IN ('UndergraduateGpa','ExamScore','ManualScore')");
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluationComponents_RawScore",
                    "[RawScore] IS NULL OR ([RawScore] >= 0 AND [RawScore] <= [MaximumRawScoreSnapshot])");
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluationComponents_NormalizedScore",
                    "[NormalizedScore] IS NULL OR [NormalizedScore] BETWEEN 0 AND 100");
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluationComponents_WeightedScore",
                    "[WeightedScore] IS NULL OR [WeightedScore] BETWEEN 0 AND 100");
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluationComponents_Weight",
                    "[WeightBasisPointsSnapshot] BETWEEN 1 AND 10000");
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluationComponents_TieBreak",
                    "[TieBreakPrioritySnapshot] > 0");
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluationComponents_SourceConfiguration",
                    "([SourceTypeSnapshot] = 'UndergraduateGpa' AND [ExamID] IS NULL AND [MaximumRawScoreSnapshot] = 4) OR ([SourceTypeSnapshot] = 'ExamScore' AND [ExamID] IS NOT NULL AND [MaximumRawScoreSnapshot] > 0) OR ([SourceTypeSnapshot] = 'ManualScore' AND [ExamID] IS NULL AND [MaximumRawScoreSnapshot] = 100)");
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluationComponents_ScoreCompleteness",
                    "([RawScore] IS NULL AND [NormalizedScore] IS NULL AND [WeightedScore] IS NULL) OR ([RawScore] IS NOT NULL AND [NormalizedScore] IS NOT NULL AND [WeightedScore] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_ApplicationEvaluationComponents_ManualAudit",
                    "([ManualScoredByAdminID] IS NULL AND [ManualScoredAtUtc] IS NULL) OR ([SourceTypeSnapshot] = 'ManualScore' AND [ManualScoredByAdminID] IS NOT NULL AND [ManualScoredAtUtc] IS NOT NULL)");
            });
            entity.HasOne(e => e.ApplicationEvaluation).WithMany(e => e.Components)
                .HasForeignKey(e => e.ApplicationEvaluationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SourceCriterion).WithMany(e => e.Components)
                .HasForeignKey(e => e.SourceCriterionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ManualScoredByAdmin).WithMany()
                .HasForeignKey(e => e.ManualScoredByAdminId).OnDelete(DeleteBehavior.Restrict);
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
                    "CK_Programs_ProgramNameEnglish_Trimmed",
                    "[ProgramNameEnglish] IS NULL OR ([ProgramNameEnglish] = LTRIM(RTRIM([ProgramNameEnglish])) AND LEN([ProgramNameEnglish]) >= 2)");
                table.HasCheckConstraint(
                    "CK_Programs_DegreeType",
                    "[DegreeType] IN (N'Doktora',N'Tezli Yüksek Lisans',N'Tezsiz Yüksek Lisans',N'Uzaktan Tezsiz Yüksek Lisans')");
            });
            entity.Property(e => e.ProgramId).HasColumnName("ProgramID");
            entity.Property(e => e.InstituteId).HasColumnName("InstituteID");
            entity.Property(e => e.ProgramName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ProgramNameEnglish).HasMaxLength(100);
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
                table.HasCheckConstraint(
                    "CK_ProgramOfferings_EvaluationState",
                    "[EvaluationState] IN ('Configuring','Finalized','Published')");
                table.HasCheckConstraint(
                    "CK_ProgramOfferings_EvaluationLifecycle",
                    "([EvaluationState] = 'Configuring' AND [EvaluationFinalizedAtUtc] IS NULL AND [ResultsPublishedAtUtc] IS NULL) OR ([EvaluationState] = 'Finalized' AND [EvaluationFinalizedAtUtc] IS NOT NULL AND [ResultsPublishedAtUtc] IS NULL) OR ([EvaluationState] = 'Published' AND [EvaluationFinalizedAtUtc] IS NOT NULL AND [ResultsPublishedAtUtc] IS NOT NULL)");
            });
            entity.Property(e => e.ProgramOfferingId).HasColumnName("ProgramOfferingID");
            entity.Property(e => e.ProgramId).HasColumnName("ProgramID");
            entity.Property(e => e.Term).HasConversion<int>();
            entity.Property(e => e.ApplicationStartUtc).HasColumnType("datetime2");
            entity.Property(e => e.ApplicationDeadlineUtc).HasColumnType("datetime2");
            entity.Property(e => e.UsesEvaluationWorkflow).HasDefaultValue(false);
            entity.Property(e => e.EvaluationState).HasConversion<string>().HasMaxLength(20).IsUnicode(false).HasDefaultValue(OfferingEvaluationState.Configuring).IsRequired();
            entity.Property(e => e.EvaluationFinalizedAtUtc).HasColumnType("datetime2");
            entity.Property(e => e.ResultsPublishedAtUtc).HasColumnType("datetime2");
            entity.Property(e => e.CreatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.UpdatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.HasOne(e => e.Program).WithMany(e => e.Offerings)
                .HasForeignKey(e => e.ProgramId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProgramOfferingEvaluationCriterion>(entity =>
        {
            entity.HasKey(e => e.CriterionId);
            entity.HasIndex(e => e.PublicId).IsUnique();
            entity.HasIndex(e => new { e.ProgramOfferingId, e.NormalizedCode }).IsUnique();
            entity.HasIndex(e => new { e.ProgramOfferingId, e.TieBreakPriority }).IsUnique();
            entity.HasIndex(e => new { e.ProgramOfferingId, e.ExamId }).IsUnique().HasFilter("[ExamID] IS NOT NULL");
            entity.HasIndex(e => new { e.ProgramOfferingId, e.SourceType }).IsUnique()
                .HasFilter("[SourceType] = 'UndergraduateGpa'");
            entity.Property(e => e.CriterionId).HasColumnName("CriterionID");
            entity.Property(e => e.PublicId).HasColumnName("PublicID").HasDefaultValueSql("NEWID()");
            entity.Property(e => e.ProgramOfferingId).HasColumnName("ProgramOfferingID");
            entity.Property(e => e.Code).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(e => e.NormalizedCode).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(150).IsRequired();
            entity.Property(e => e.SourceType).HasConversion<string>().HasMaxLength(32).IsUnicode(false).IsRequired();
            entity.Property(e => e.ExamId).HasColumnName("ExamID");
            entity.Property(e => e.MaximumRawScore).HasColumnType("decimal(9, 4)");
            entity.Property(e => e.CreatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.UpdatedAtUtc).HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.ToTable("ProgramOfferingEvaluationCriteria", table =>
            {
                table.HasCheckConstraint(
                    "CK_ProgramOfferingEvaluationCriteria_SourceType",
                    "[SourceType] IN ('UndergraduateGpa','ExamScore','ManualScore')");
                table.HasCheckConstraint(
                    "CK_ProgramOfferingEvaluationCriteria_SourceConfiguration",
                    "([SourceType] = 'UndergraduateGpa' AND [ExamID] IS NULL AND [MaximumRawScore] = 4) OR ([SourceType] = 'ExamScore' AND [ExamID] IS NOT NULL AND [MaximumRawScore] > 0) OR ([SourceType] = 'ManualScore' AND [ExamID] IS NULL AND [MaximumRawScore] = 100)");
                table.HasCheckConstraint(
                    "CK_ProgramOfferingEvaluationCriteria_Weight",
                    "[WeightBasisPoints] BETWEEN 1 AND 10000");
                table.HasCheckConstraint(
                    "CK_ProgramOfferingEvaluationCriteria_TieBreak",
                    "[TieBreakPriority] > 0");
                table.HasCheckConstraint(
                    "CK_ProgramOfferingEvaluationCriteria_Code",
                    "[Code] = LTRIM(RTRIM([Code])) AND LEN([Code]) BETWEEN 2 AND 64");
            });
            entity.HasOne(e => e.ProgramOffering).WithMany(e => e.EvaluationCriteria)
                .HasForeignKey(e => e.ProgramOfferingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Exam).WithMany()
                .HasForeignKey(e => e.ExamId).OnDelete(DeleteBehavior.Restrict);
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
