namespace PicSelect.Core.Projects;

public enum ProjectImportStatus
{
    Pending,
    Scanning,
    Importing,
    Completed,
    Canceled,
    Interrupted,
    Failed,
}

public sealed record CreatedProject(long ProjectId, string FolderPath, ProjectImportStatus ImportStatus, bool AlreadyExisted);

public sealed record ImportedProject(long ProjectId, string FolderPath, int ImportedPhotoCount, bool AlreadyExisted);

public sealed record ProjectImportProgress(
    long ProjectId,
    ProjectImportStatus ImportStatus,
    int ImportedPhotoCount,
    TimeSpan Elapsed);

public sealed record ProjectSummary(
    long ProjectId,
    string FolderPath,
    string DisplayName,
    DateTimeOffset ImportedAtUtc,
    ProjectImportStatus ImportStatus,
    int PhotoCount,
    int IterationCount)
{
    public bool IsReviewAvailable => ImportStatus == ProjectImportStatus.Completed;
}

public sealed record ProjectOverview(
    long ProjectId,
    string FolderPath,
    string DisplayName,
    DateTimeOffset ImportedAtUtc,
    ProjectImportStatus ImportStatus,
    IReadOnlyList<IterationSummary> Iterations)
{
    public bool IsReviewAvailable => ImportStatus == ProjectImportStatus.Completed;
}

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
    string RelativePath,
    string FileName,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc,
    string? DecisionType);

public sealed record ReviewSession(
    long ProjectId,
    long IterationId,
    int IterationNumber,
    int CurrentPhotoIndex,
    int ReviewedPhotoCount,
    IReadOnlyList<IterationPhoto> Photos)
{
    public IterationPhoto CurrentPhoto => Photos[CurrentPhotoIndex];
}
