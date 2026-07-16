using System;
using System.Collections.Generic;

namespace GraduateApp.API.Models;

public partial class University
{
    public int UniversityId { get; set; }

    public string UniversityName { get; set; } = null!;

    public virtual ICollection<EducationInfo> EducationInfos { get; set; } = new List<EducationInfo>();
}
