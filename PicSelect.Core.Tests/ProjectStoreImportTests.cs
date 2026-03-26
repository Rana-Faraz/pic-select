using PicSelect.Core.Projects;

namespace PicSelect.Core.Tests;

public sealed class ProjectStoreImportTests
{
    [Fact]
    public async Task ImportProjectFromFolderAsync_RecursivelyCreatesSnapshotAndSeedsIterationOne()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("shoot");
        workspace.WriteFile(Path.Combine(sourceFolder, "b.png"));
        workspace.WriteFile(Path.Combine(sourceFolder, "a.jpg"));
        workspace.WriteFile(Path.Combine(sourceFolder, "notes.txt"));
        workspace.WriteFile(Path.Combine(sourceFolder, "nested", "c.jpg"));
        workspace.WriteFile(Path.Combine(sourceFolder, "nested", "deep", "d.webp"));

        var store = new PicSelectStore(workspace.DatabasePath);

        var importedProject = await store.ImportProjectFromFolderAsync(sourceFolder);

        Assert.Equal(4, importedProject.ImportedPhotoCount);

        var project = await store.GetProjectOverviewAsync(importedProject.ProjectId);
        Assert.NotNull(project);
        Assert.Equal(sourceFolder, project.FolderPath);

        var iteration = Assert.Single(project.Iterations);
        Assert.Equal(1, iteration.Number);
        Assert.Equal(4, iteration.TotalPhotoCount);
        Assert.Equal(0, iteration.ReviewedPhotoCount);

        var photos = await store.GetIterationPhotosAsync(importedProject.ProjectId, 1);
        Assert.Equal(
            new[]
            {
                "a.jpg",
                "b.png",
                Path.Combine("nested", "c.jpg"),
                Path.Combine("nested", "deep", "d.webp"),
            },
            photos.Select(photo => photo.RelativePath));

        Assert.True(File.Exists(Path.Combine(sourceFolder, "a.jpg")));
        Assert.True(File.Exists(Path.Combine(sourceFolder, "b.png")));
    }

    [Fact]
    public async Task GetProjectsAsync_ReturnsPersistedProjectAfterStoreIsRecreated()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("catalog");
        workspace.WriteFile(Path.Combine(sourceFolder, "cover.jpeg"));

        var firstStore = new PicSelectStore(workspace.DatabasePath);
        var importedProject = await firstStore.ImportProjectFromFolderAsync(sourceFolder);

        var secondStore = new PicSelectStore(workspace.DatabasePath);
        var projects = await secondStore.GetProjectsAsync();

        var project = Assert.Single(projects);
        Assert.Equal(importedProject.ProjectId, project.ProjectId);
        Assert.Equal(sourceFolder, project.FolderPath);
        Assert.Equal(1, project.PhotoCount);
        Assert.Equal(1, project.IterationCount);
    }

    [Fact]
    public async Task GetProjectOverviewAsync_LoadsIterationSummaryAfterStoreIsRecreated()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("detail");
        workspace.WriteFile(Path.Combine(sourceFolder, "frame-02.png"));
        workspace.WriteFile(Path.Combine(sourceFolder, "frame-01.png"));

        var firstStore = new PicSelectStore(workspace.DatabasePath);
        var importedProject = await firstStore.ImportProjectFromFolderAsync(sourceFolder);

        var secondStore = new PicSelectStore(workspace.DatabasePath);
        var overview = await secondStore.GetProjectOverviewAsync(importedProject.ProjectId);

        Assert.NotNull(overview);
        var iteration = Assert.Single(overview.Iterations);
        Assert.Equal(1, iteration.Number);
        Assert.Equal(2, iteration.TotalPhotoCount);
        Assert.Equal(0, iteration.ReviewedPhotoCount);
    }

    [Fact]
    public async Task ImportProjectFromFolderAsync_ReportsRealProgressStates()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("progress");
        workspace.WriteFile(Path.Combine(sourceFolder, "frame-02.png"));
        workspace.WriteFile(Path.Combine(sourceFolder, "nested", "frame-01.png"));

        var store = new PicSelectStore(workspace.DatabasePath);
        var updates = new List<ProjectImportProgress>();

        var importedProject = await store.ImportProjectFromFolderAsync(
            sourceFolder,
            new CollectingProgress<ProjectImportProgress>(updates));

        Assert.Equal(importedProject.ImportedPhotoCount, updates[^1].ImportedPhotoCount);
        Assert.Contains(updates, update => update.ImportStatus == ProjectImportStatus.Scanning);
        Assert.Contains(updates, update => update.ImportStatus == ProjectImportStatus.Importing);
        Assert.Equal(ProjectImportStatus.Completed, updates[^1].ImportStatus);
        Assert.True(updates.Select(update => update.ImportedPhotoCount).SequenceEqual(
            updates.Select(update => update.ImportedPhotoCount).OrderBy(count => count)));
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string rootPath = Path.Combine(Path.GetTempPath(), "PicSelect.Tests", Guid.NewGuid().ToString("N"));

        public TestWorkspace()
        {
            Directory.CreateDirectory(rootPath);
        }

        public string DatabasePath => Path.Combine(rootPath, "picselect.db");

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public void WriteFile(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "test");
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private sealed class CollectingProgress<T>(ICollection<T> values) : IProgress<T>
    {
        public void Report(T value)
        {
            values.Add(value);
        }
    }
}
