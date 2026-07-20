using System.Data;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GraduateApp.API.Services;

public interface IAdminStudentService
{
    Task<PagedResult<AdminStudentListItemDto>> GetAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
    Task<AdminStudentDetailDto?> GetDetailAsync(string tc, CancellationToken cancellationToken);
    Task<ServiceResult> DeactivateAsync(string tc, int adminId, CancellationToken cancellationToken);
}

public sealed class AdminStudentService(
    GraduateAppDbContext dbContext,
    TimeProvider timeProvider) : IAdminStudentService
{
    public async Task<PagedResult<AdminStudentListItemDto>> GetAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var query = dbContext.Students.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(item =>
                item.Tc.Contains(term)
                || item.StudentName.Contains(term)
                || item.StudentSurname.Contains(term)
                || item.Email.Contains(term));
        }

        var now = timeProvider.GetUtcNow();
        var totalCount = await query.CountAsync(cancellationToken);
        var students = await query
            .OrderBy(item => item.StudentSurname)
            .ThenBy(item => item.StudentName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var items = students.Select(item => new AdminStudentListItemDto(
                item.Tc,
                MaskTc(item.Tc),
                (item.StudentName + " " + item.StudentSurname).Trim(),
                item.Email,
                item.LockoutEndUtc == null || item.LockoutEndUtc <= now,
                item.UpdatedAtUtc))
            .ToArray();
        return new PagedResult<AdminStudentListItemDto>(items, page, pageSize, totalCount);
    }

    public async Task<AdminStudentDetailDto?> GetDetailAsync(
        string tc,
        CancellationToken cancellationToken)
    {
        var student = await dbContext.Students.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Tc == tc, cancellationToken);
        if (student is null)
        {
            return null;
        }

        var applicationCount = await dbContext.Applications.CountAsync(item => item.Tc == tc, cancellationToken);
        var examScoreCount = await dbContext.StudentExamScores.CountAsync(item => item.Tc == tc, cancellationToken);
        var now = timeProvider.GetUtcNow();
        return new AdminStudentDetailDto(
            student.Tc,
            MaskTc(student.Tc),
            $"{student.StudentName} {student.StudentSurname}".Trim(),
            student.Email,
            student.Telephone,
            student.LockoutEndUtc == null || student.LockoutEndUtc <= now,
            applicationCount,
            examScoreCount,
            student.UpdatedAtUtc);
    }

    public async Task<ServiceResult> DeactivateAsync(
        string tc,
        int adminId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        var student = await dbContext.Students.SingleOrDefaultAsync(item => item.Tc == tc, cancellationToken);
        if (student is null)
        {
            return ServiceResult.Failure("Öğrenci bulunamadı.", StatusCodes.Status404NotFound);
        }

        var now = timeProvider.GetUtcNow();
        student.LockoutEndUtc = DateTimeOffset.MaxValue;
        student.SecurityStamp = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        student.AccessFailedCount = 0;
        student.UpdatedAtUtc = now.UtcDateTime;

        var resetTokens = await dbContext.PasswordResetTokens
            .Where(item => item.Tc == tc && !item.IsUsed)
            .ToListAsync(cancellationToken);
        foreach (var token in resetTokens)
        {
            token.IsUsed = true;
        }

        dbContext.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            ActorAdminId = adminId,
            EventType = "StudentDeactivated",
            TargetType = "Student",
            TargetId = tc,
            Details = "Öğrenci pasifleştirildi ve mevcut oturumları iptal edildi.",
            CreatedAtUtc = now.UtcDateTime
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ServiceResult.Success();
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private static string MaskTc(string tc) => tc.Length >= 4 ? $"*******{tc[^4..]}" : "***********";
}
