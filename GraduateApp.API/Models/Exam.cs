using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class Exam
{
    public int ExamId { get; set; }

    public string ExamName { get; set; } = null!;

    public virtual ICollection<StudentExamScore> StudentExamScores { get; set; } = new List<StudentExamScore>();
}
