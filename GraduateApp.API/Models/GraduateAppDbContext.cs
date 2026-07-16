using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Models;

public partial class GraduateAppDbContext : DbContext
{
    public GraduateAppDbContext()
    {
    }

    public GraduateAppDbContext(DbContextOptions<GraduateAppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Admin> Admins { get; set; }

    public virtual DbSet<Application> Applications { get; set; }

    public virtual DbSet<ApplicationStatusHistory> ApplicationStatusHistories { get; set; }

    public virtual DbSet<EducationInfo> EducationInfos { get; set; }

    public virtual DbSet<Exam> Exams { get; set; }

    public virtual DbSet<Institute> Institutes { get; set; }

    public virtual DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

    public virtual DbSet<Program> Programs { get; set; }

    public virtual DbSet<ReferenceLetter> ReferenceLetters { get; set; }

    public virtual DbSet<Student> Students { get; set; }

    public virtual DbSet<StudentExamScore> StudentExamScores { get; set; }

    public virtual DbSet<SystemLog> SystemLogs { get; set; }

    public virtual DbSet<University> Universities { get; set; }

    public virtual DbSet<VwAdminApplicationSummary> VwAdminApplicationSummaries { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer("Name=ConnectionStrings:DefaultConnection");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Admin>(entity =>
        {
            entity.HasKey(e => e.AdminId).HasName("PK__Admins__719FE4E846A39A1A");

            entity.HasIndex(e => e.Email, "UQ__Admins__A9D10534B7AA4311").IsUnique();

            entity.Property(e => e.AdminId).HasColumnName("AdminID");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(256)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasKey(e => e.ApplicationId).HasName("PK__Applicat__C93A4F79B234BC7C");

            entity.Property(e => e.ApplicationId).HasColumnName("ApplicationID");
            entity.Property(e => e.ApplicationDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CurrentStatus)
                .HasMaxLength(50)
                .HasDefaultValue("Sisteme Alındı");
            entity.Property(e => e.ProgramId).HasColumnName("ProgramID");
            entity.Property(e => e.Tc)
                .HasMaxLength(11)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("TC");

            entity.HasOne(d => d.Program).WithMany(p => p.Applications)
                .HasForeignKey(d => d.ProgramId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Applicati__Progr__5441852A");

            entity.HasOne(d => d.TcNavigation).WithMany(p => p.Applications)
                .HasForeignKey(d => d.Tc)
                .HasConstraintName("FK__Applications__TC__534D60F1");
        });

        modelBuilder.Entity<ApplicationStatusHistory>(entity =>
        {
            entity.HasKey(e => e.HistoryId).HasName("PK__Applicat__4D7B4ADDA0D0035E");

            entity.ToTable("ApplicationStatusHistory", tb => tb.HasTrigger("trg_UpdateApplicationStatus"));

            entity.Property(e => e.HistoryId).HasColumnName("HistoryID");
            entity.Property(e => e.ApplicationId).HasColumnName("ApplicationID");
            entity.Property(e => e.ChangeDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.ChangedByAdminId).HasColumnName("ChangedByAdminID");
            entity.Property(e => e.StatusName).HasMaxLength(50);

            entity.HasOne(d => d.Application).WithMany(p => p.ApplicationStatusHistories)
                .HasForeignKey(d => d.ApplicationId)
                .HasConstraintName("FK__Applicati__Appli__5AEE82B9");

            entity.HasOne(d => d.ChangedByAdmin).WithMany(p => p.ApplicationStatusHistories)
                .HasForeignKey(d => d.ChangedByAdminId)
                .HasConstraintName("FK__Applicati__Chang__5BE2A6F2");
        });

        modelBuilder.Entity<EducationInfo>(entity =>
        {
            entity.HasKey(e => e.EducationId).HasName("PK__Educatio__4BBE38E5F8870634");

            entity.ToTable("EducationInfo");

            entity.Property(e => e.EducationId).HasColumnName("EducationID");
            entity.Property(e => e.DiplomaPath).HasMaxLength(255);
            entity.Property(e => e.Faculty).HasMaxLength(100);
            entity.Property(e => e.Gno)
                .HasColumnType("decimal(3, 2)")
                .HasColumnName("GNO");
            entity.Property(e => e.GraduatedProgram).HasMaxLength(100);
            entity.Property(e => e.Tc)
                .HasMaxLength(11)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("TC");
            entity.Property(e => e.TranscriptPath).HasMaxLength(255);
            entity.Property(e => e.UniversityId).HasColumnName("UniversityID");

            entity.HasOne(d => d.TcNavigation).WithMany(p => p.EducationInfos)
                .HasForeignKey(d => d.Tc)
                .HasConstraintName("FK__EducationInf__TC__49C3F6B7");

            entity.HasOne(d => d.University).WithMany(p => p.EducationInfos)
                .HasForeignKey(d => d.UniversityId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Education__Unive__4AB81AF0");
        });

        modelBuilder.Entity<Exam>(entity =>
        {
            entity.HasKey(e => e.ExamId).HasName("PK__Exams__297521A751DF53F5");

            entity.HasIndex(e => e.ExamName, "UQ__Exams__052792806EAE5CB7").IsUnique();

            entity.Property(e => e.ExamId).HasColumnName("ExamID");
            entity.Property(e => e.ExamName).HasMaxLength(50);
        });

        modelBuilder.Entity<Institute>(entity =>
        {
            entity.HasKey(e => e.InstituteId).HasName("PK__Institut__09EC0D9BC73D41E4");

            entity.HasIndex(e => e.InstituteName, "UQ__Institut__D3D6784CC533B89F").IsUnique();

            entity.Property(e => e.InstituteId).HasColumnName("InstituteID");
            entity.Property(e => e.InstituteName).HasMaxLength(100);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.TokenId).HasName("PK__Password__658FEE8A59010694");

            entity.HasIndex(e => e.TokenHash, "UQ__Password__BCB33F926A8813CE").IsUnique();

            entity.Property(e => e.TokenId).HasColumnName("TokenID");
            entity.Property(e => e.ExpirationDate).HasColumnType("datetime");
            entity.Property(e => e.IsUsed).HasDefaultValue(false);
            entity.Property(e => e.Tc)
                .HasMaxLength(11)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("TC");
            entity.Property(e => e.TokenHash)
                .HasMaxLength(256)
                .IsUnicode(false);

            entity.HasOne(d => d.TcNavigation).WithMany(p => p.PasswordResetTokens)
                .HasForeignKey(d => d.Tc)
                .HasConstraintName("FK__PasswordRese__TC__60A75C0F");
        });

        modelBuilder.Entity<Program>(entity =>
        {
            entity.HasKey(e => e.ProgramId).HasName("PK__Programs__75256038BF693190");

            entity.Property(e => e.ProgramId).HasColumnName("ProgramID");
            entity.Property(e => e.DegreeType).HasMaxLength(50);
            entity.Property(e => e.InstituteId).HasColumnName("InstituteID");
            entity.Property(e => e.ProgramName).HasMaxLength(100);

            entity.HasOne(d => d.Institute).WithMany(p => p.Programs)
                .HasForeignKey(d => d.InstituteId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Programs__Instit__3D5E1FD2");
        });

        modelBuilder.Entity<ReferenceLetter>(entity =>
        {
            entity.HasKey(e => e.ReferenceId).HasName("PK__Referenc__E1A99A79647995CB");

            entity.Property(e => e.ReferenceId).HasColumnName("ReferenceID");
            entity.Property(e => e.AcademicianEmail).HasMaxLength(100);
            entity.Property(e => e.AcademicianName).HasMaxLength(100);
            entity.Property(e => e.ApplicationId).HasColumnName("ApplicationID");
            entity.Property(e => e.FilePath).HasMaxLength(255);

            entity.HasOne(d => d.Application).WithMany(p => p.ReferenceLetters)
                .HasForeignKey(d => d.ApplicationId)
                .HasConstraintName("FK__Reference__Appli__571DF1D5");
        });

        modelBuilder.Entity<Student>(entity =>
        {
            entity.HasKey(e => e.Tc).HasName("PK__Students__3214E408F304E032");

            entity.HasIndex(e => e.Email, "UQ__Students__A9D10534C8AFDCEF").IsUnique();

            entity.HasIndex(e => e.Telephone, "UQ__Students__D9FEB744290C1415").IsUnique();

            entity.Property(e => e.Tc)
                .HasMaxLength(11)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("TC");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.FatherName).HasMaxLength(50);
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(256)
                .IsUnicode(false);
            entity.Property(e => e.StudentName).HasMaxLength(50);
            entity.Property(e => e.StudentSurname).HasMaxLength(50);
            entity.Property(e => e.Telephone)
                .HasMaxLength(15)
                .IsUnicode(false);
        });

        modelBuilder.Entity<StudentExamScore>(entity =>
        {
            entity.HasKey(e => e.ScoreId).HasName("PK__StudentE__7DD229F139B5F4A3");

            entity.Property(e => e.ScoreId).HasColumnName("ScoreID");
            entity.Property(e => e.ExamId).HasColumnName("ExamID");
            entity.Property(e => e.Score).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.Tc)
                .HasMaxLength(11)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("TC");

            entity.HasOne(d => d.Exam).WithMany(p => p.StudentExamScores)
                .HasForeignKey(d => d.ExamId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StudentEx__ExamI__4E88ABD4");

            entity.HasOne(d => d.TcNavigation).WithMany(p => p.StudentExamScores)
                .HasForeignKey(d => d.Tc)
                .HasConstraintName("FK__StudentExamS__TC__4D94879B");
        });

        modelBuilder.Entity<SystemLog>(entity =>
        {
            entity.HasKey(e => e.LogId).HasName("PK__SystemLo__5E5499A86F8BC0BB");

            entity.Property(e => e.LogId).HasColumnName("LogID");
            entity.Property(e => e.ActionType).HasMaxLength(50);
            entity.Property(e => e.AdminId).HasColumnName("AdminID");
            entity.Property(e => e.LogDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.LogDetails).HasColumnType("xml");
            entity.Property(e => e.Tc)
                .HasMaxLength(11)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("TC");

            entity.HasOne(d => d.Admin).WithMany(p => p.SystemLogs)
                .HasForeignKey(d => d.AdminId)
                .HasConstraintName("FK__SystemLog__Admin__6477ECF3");

            entity.HasOne(d => d.TcNavigation).WithMany(p => p.SystemLogs)
                .HasForeignKey(d => d.Tc)
                .HasConstraintName("FK__SystemLogs__TC__656C112C");
        });

        modelBuilder.Entity<University>(entity =>
        {
            entity.HasKey(e => e.UniversityId).HasName("PK__Universi__9F19E19C8FB6817C");

            entity.HasIndex(e => e.UniversityName, "UQ__Universi__53F0B53C239FB43C").IsUnique();

            entity.Property(e => e.UniversityId).HasColumnName("UniversityID");
            entity.Property(e => e.UniversityName).HasMaxLength(100);
        });

        modelBuilder.Entity<VwAdminApplicationSummary>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_AdminApplicationSummary");

            entity.Property(e => e.AdSoyad).HasMaxLength(101);
            entity.Property(e => e.BasvuruTarihi).HasColumnType("datetime");
            entity.Property(e => e.Enstitu).HasMaxLength(100);
            entity.Property(e => e.Eposta).HasMaxLength(100);
            entity.Property(e => e.GuncelDurum).HasMaxLength(50);
            entity.Property(e => e.KimlikNo)
                .HasMaxLength(11)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.LisansOrtalamasi).HasColumnType("decimal(3, 2)");
            entity.Property(e => e.MezunOlduguUniversite).HasMaxLength(100);
            entity.Property(e => e.Program).HasMaxLength(100);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
