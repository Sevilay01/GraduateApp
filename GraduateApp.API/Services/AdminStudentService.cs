using System.Data;
using System.Security.Cryptography;
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
    Task<AdminStudentDetailDto?> GetDetailAsync(Guid publicId, CancellationToken cancellationToken);
    Task<ServiceResult> DeactivateAsync(Guid publicId, int adminId, CancellationToken cancellationToken);
    Task<ServiceResult> ActivateAsync(Guid publicId, int adminId, CancellationToken cancellationToken);
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
                item.StudentName.Contains(term)
                || item.StudentSurname.Contains(term)
                || item.Email.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var students = await query
            .OrderBy(item => item.StudentSurname)
            .ThenBy(item => item.StudentName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var items = students.Select(item => new AdminStudentListItemDto(
                item.PublicId,
                MaskTc(item.Tc),
                (item.StudentName + " " + item.StudentSurname).Trim(),
                item.Email,
                item.IsActive,
                item.UpdatedAtUtc))
            .ToArray();
        return new PagedResult<AdminStudentListItemDto>(items, page, pageSize, totalCount);
    }

    public async Task<AdminStudentDetailDto?> GetDetailAsync(
        Guid publicId,
        CancellationToken cancellationToken)
    {
        var student = await dbContext.Students.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PublicId == publicId, cancellationToken);
        if (student is null)
        {
            return null;
        }

        var applicationCount = await dbContext.Applications.CountAsync(
            item => item.Tc == student.Tc,
            cancellationToken);
        var examScoreCount = await dbContext.StudentExamScores.CountAsync(
            item => item.Tc == student.Tc,
            cancellationToken);
        return new AdminStudentDetailDto(
            student.PublicId,
            MaskTc(student.Tc),
            $"{student.StudentName} {student.StudentSurname}".Trim(),
            student.Email,
            student.Telephone,
            student.IsActive,
            applicationCount,
            examScoreCount,
            student.UpdatedAtUtc);
    }

    public async Task<ServiceResult> DeactivateAsync(
        Guid publicId,
        int adminId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        var student = await dbContext.Students.SingleOrDefaultAsync(
            item => item.PublicId == publicId,
            cancellationToken);
        if (student is null)
        {
            return ServiceResult.Failure("Öğrenci bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (!student.IsActive)
        {
            return ServiceResult.Failure("Öğrenci zaten pasif.", StatusCodes.Status409Conflict);
        }

        var now = timeProvider.GetUtcNow();
        student.IsActive = false;
        student.SecurityStamp = NewSecurityStamp();
        student.AccessFailedCount = 0;
        student.UpdatedAtUtc = now.UtcDateTime;

        var resetTokens = await dbContext.PasswordResetTokens
            .Where(item => item.Tc == student.Tc && !item.IsUsed)
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
            TargetId = publicId.ToString("D"),
            Details = "Öğrenci pasifleştirildi ve mevcut oturumları iptal edildi.",
            CreatedAtUtc = now.UtcDateTime
        });

        return await SaveOperationAsync(transaction, cancellationToken);
    }

    public async Task<ServiceResult> ActivateAsync(
        Guid publicId,
        int adminId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        var student = await dbContext.Students.SingleOrDefaultAsync(
            item => item.PublicId == publicId,
            cancellationToken);
        if (student is null)
        {
            return ServiceResult.Failure("Öğrenci bulunamadı.", StatusCodes.Status404NotFound);
        }

        if (student.IsActive)
        {
            return ServiceResult.Failure("Öğrenci zaten aktif.", StatusCodes.Status409Conflict);
        }

        var now = timeProvider.GetUtcNow();
        student.IsActive = true;
        student.SecurityStamp = NewSecurityStamp();
        student.AccessFailedCount = 0;
        student.UpdatedAtUtc = now.UtcDateTime;

        dbContext.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            ActorAdminId = adminId,
            EventType = "StudentActivated",
            TargetType = "Student",
            TargetId = publicId.ToString("D"),
            Details = "Öğrenci yeniden aktifleştirildi ve önceki oturumları geçersiz kılındı.",
            CreatedAtUtc = now.UtcDateTime
        });

        return await SaveOperationAsync(transaction, cancellationToken);
    }

    private async Task<ServiceResult> SaveOperationAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
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

    private static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string MaskTc(string tc) => tc.Length >= 4 ? $"*******{tc[^4..]}" : "***********";
}
