using System.Runtime.InteropServices.WindowsRuntime;
using PicSelect.Core.Projects;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PicSelect.Services;

public sealed class ThumbnailCacheService
{
    private readonly object gate = new();
    private readonly Dictionary<long, Task> activeJobs = new();
    private readonly string cacheRootPath;
    private readonly PicSelectStore store;

    public ThumbnailCacheService(PicSelectStore store, string cacheRootPath)
    {
        this.store = store;
        this.cacheRootPath = cacheRootPath;
        Directory.CreateDirectory(cacheRootPath);
    }

    public bool IsThumbnailingActive(long projectId)
    {
        lock (gate)
        {
            return activeJobs.ContainsKey(projectId);
        }
    }

    public void StartThumbnailGeneration(long projectId)
    {
        lock (gate)
        {
            if (activeJobs.ContainsKey(projectId))
            {
                return;
            }

            activeJobs[projectId] = RunThumbnailGenerationAsync(projectId);
        }
    }

    public Uri? GetThumbnailUri(long projectId, long photoId)
    {
        var thumbnailPath = GetThumbnailPath(projectId, photoId);
        return File.Exists(thumbnailPath) ? new Uri(thumbnailPath) : null;
    }

    public void DeleteProjectCache(long projectId)
    {
        var projectCachePath = GetProjectCachePath(projectId);
        if (!Directory.Exists(projectCachePath))
        {
            return;
        }

        Directory.Delete(projectCachePath, recursive: true);
    }

    private async Task RunThumbnailGenerationAsync(long projectId)
    {
        try
        {
            Directory.CreateDirectory(GetProjectCachePath(projectId));

            var photos = await store.GetIterationPhotosAsync(projectId, 1);
            foreach (var photo in photos)
            {
                var thumbnailPath = GetThumbnailPath(projectId, photo.PhotoId);
                if (File.Exists(thumbnailPath))
                {
                    continue;
                }

                try
                {
                    await CreateThumbnailAsync(photo.FilePath, thumbnailPath);
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Debug.WriteLine($"Thumbnail generation failed for {photo.FilePath}: {exception}");
                }
            }
        }
        finally
        {
            lock (gate)
            {
                activeJobs.Remove(projectId);
            }
        }
    }

    private async Task CreateThumbnailAsync(string sourceFilePath, string thumbnailPath)
    {
        var sourceFile = await StorageFile.GetFileFromPathAsync(sourceFilePath);
        using var thumbnail = await sourceFile.GetThumbnailAsync(
            ThumbnailMode.PicturesView,
            320,
            ThumbnailOptions.ResizeThumbnail);

        if (thumbnail is null || thumbnail.Size == 0)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
        await using var outputStream = new FileStream(thumbnailPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var inputStream = thumbnail.AsStreamForRead();
        await inputStream.CopyToAsync(outputStream);
    }

    private string GetProjectCachePath(long projectId) => Path.Combine(cacheRootPath, projectId.ToString());

    private string GetThumbnailPath(long projectId, long photoId) => Path.Combine(GetProjectCachePath(projectId), $"{photoId}.jpg");
}
