using PicSelect.Core.Projects;

namespace PicSelect.Core.Tests;

public sealed class ProjectImportLifecycleTests
{
    [Fact]
    public async Task CreateProjectAsync_PersistsPendingProjectBeforeImportStarts()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("pending");

        var store = new PicSelectStore(workspace.DatabasePath);

        var createdProject = await store.CreateProjectAsync(sourceFolder);
        var projects = await store.GetProjectsAsync();
        var overview = await store.GetProjectOverviewAsync(createdProject.ProjectId);

        var project = Assert.Single(projects);
        Assert.Equal(createdProject.ProjectId, project.ProjectId);
        Assert.Equal(ProjectImportStatus.Pending, project.ImportStatus);
        Assert.Equal(0, project.PhotoCount);
        Assert.Equal(0, project.IterationCount);

        Assert.NotNull(overview);
        Assert.Equal(ProjectImportStatus.Pending, overview.ImportStatus);
        Assert.Empty(overview.Iterations);
    }

    [Fact]
    public async Task GetReviewSessionAsync_ReturnsNullForIncompleteProject()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("blocked");

        var store = new PicSelectStore(workspace.DatabasePath);
        var createdProject = await store.CreateProjectAsync(sourceFolder);

        var session = await store.GetReviewSessionAsync(createdProject.ProjectId, 1);

        Assert.Null(session);
    }

    [Fact]
    public async Task ImportProjectFromFolderAsync_MarksProjectAsCompleted()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("complete");
        workspace.WriteFile(Path.Combine(sourceFolder, "a.jpg"));

        var store = new PicSelectStore(workspace.DatabasePath);

        var importedProject = await store.ImportProjectFromFolderAsync(sourceFolder);
        var overview = await store.GetProjectOverviewAsync(importedProject.ProjectId);

        Assert.NotNull(overview);
        Assert.Equal(ProjectImportStatus.Completed, overview.ImportStatus);
        Assert.Single(overview.Iterations);
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
}
