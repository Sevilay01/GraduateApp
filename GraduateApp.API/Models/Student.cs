using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class Student
{
    public string Tc { get; set; } = null!;

    public string StudentName { get; set; } = null!;

    public string StudentSurname { get; set; } = null!;

    public string? FatherName { get; set; }

    public DateOnly? BirthDate { get; set; }

    public string Email { get; set; } = null!;

    public string? Telephone { get; set; }

    public string PasswordHash { get; set; } = null!;

    public virtual ICollection<Application> Applications { get; set; } = new List<Application>();

    public virtual ICollection<EducationInfo> EducationInfos { get; set; } = new List<EducationInfo>();

    public virtual ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();

    public virtual ICollection<StudentExamScore> StudentExamScores { get; set; } = new List<StudentExamScore>();

    public virtual ICollection<SystemLog> SystemLogs { get; set; } = new List<SystemLog>();
}
