using System.Data;
using GraduateApp.API.DTOs;
using GraduateApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GraduateApp.API.Services;

public interface IUniversityCatalogService
{
    Task<IReadOnlyList<UniversityDto>> GetAsync(CancellationToken cancellationToken);
    Task<ServiceResult<UniversityDto>> CreateAsync(
        int adminId,
        UniversityCreateDto request,
        CancellationToken cancellationToken);
}

public sealed class UniversityCatalogService(
    GraduateAppDbContext dbContext,
    TimeProvider timeProvider) : IUniversityCatalogService
{
    public async Task<IReadOnlyList<UniversityDto>> GetAsync(CancellationToken cancellationToken) =>
        await dbContext.Universities.AsNoTracking()
            .OrderBy(item => item.UniversityName)
            .ThenBy(item => item.UniversityId)
            .Select(item => new UniversityDto(item.UniversityId, item.UniversityName))
            .ToListAsync(cancellationToken);

    public async Task<ServiceResult<UniversityDto>> CreateAsync(
        int adminId,
        UniversityCreateDto request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateName(request.UniversityName);
        if (!validation.IsSuccess)
        {
            return ServiceResult<UniversityDto>.Failure(validation.Error!, validation.StatusCode);
        }

        var name = validation.Value!;
        if (await dbContext.Universities.AnyAsync(
            item => item.UniversityName == name,
            cancellationToken))
        {
            return DuplicateFailure();
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        var university = new University { UniversityName = name };
        dbContext.Universities.Add(university);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.SecurityAuditLogs.Add(new SecurityAuditLog
            {
                ActorAdminId = adminId,
                EventType = "UniversityCreated",
                TargetType = "University",
                TargetId = university.UniversityId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                Details = "{}",
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ServiceResult<UniversityDto>.Success(
                new UniversityDto(university.UniversityId, university.UniversityName),
                StatusCodes.Status201Created);
        }
        catch (DbUpdateException exception)
            when (DatabaseExceptionClassifier.IsUniqueConstraintViolation(exception)
                && !DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return DuplicateFailure();
        }
        catch (DbUpdateException exception) when (!DatabaseExceptionClassifier.IsUnavailable(exception))
        {
            return ServiceResult<UniversityDto>.Failure(
                "Üniversite şu anda oluşturulamıyor. Lütfen daha sonra tekrar deneyin.",
                StatusCodes.Status500InternalServerError);
        }
    }

    private static ServiceResult<string> ValidateName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 100 || name.Any(char.IsControl))
        {
            return ServiceResult<string>.Failure(
                "Üniversite adı 2 ile 100 karakter arasında olmalı ve kontrol karakteri içermemelidir.",
                StatusCodes.Status400BadRequest);
        }

        return ServiceResult<string>.Success(name);
    }

    private static ServiceResult<UniversityDto> DuplicateFailure() =>
        ServiceResult<UniversityDto>.Failure(
            "Aynı adda bir üniversite zaten bulunuyor.",
            StatusCodes.Status409Conflict);
}
