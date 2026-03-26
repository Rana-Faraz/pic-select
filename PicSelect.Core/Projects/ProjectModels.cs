namespace PicSelect.Core.Projects;

public sealed record ImportedProject(long ProjectId, string FolderPath, int ImportedPhotoCount, bool AlreadyExisted);

public sealed record ProjectSummary(
    long ProjectId,
    string FolderPath,
    string DisplayName,
    DateTimeOffset ImportedAtUtc,
    int PhotoCount,
    int IterationCount);

public sealed record ProjectOverview(
    long ProjectId,
    string FolderPath,
    string DisplayName,
    DateTimeOffset ImportedAtUtc,
    IReadOnlyList<IterationSummary> Iterations);

public sealed record IterationSummary(
    long IterationId,
    int Number,
    int TotalPhotoCount,
    int ReviewedPhotoCount,
    int ChosenPhotoCount,
    int IgnoredPhotoCount);

public sealed record IterationPhoto(
    long PhotoId,
    string FilePath,
    string FileName,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc,
    string? DecisionType);
