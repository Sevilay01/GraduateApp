using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class StudentExamScore
{
    public int ScoreId { get; set; }

    public string Tc { get; set; } = null!;

    public int ExamId { get; set; }

    public decimal Score { get; set; }

    public DateOnly? ExamDate { get; set; }

    public virtual Exam Exam { get; set; } = null!;

    public virtual Student TcNavigation { get; set; } = null!;
}
