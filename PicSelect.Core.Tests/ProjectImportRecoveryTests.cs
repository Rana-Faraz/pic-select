using PicSelect.Core.Projects;

namespace PicSelect.Core.Tests;

public sealed class ProjectImportRecoveryTests
{
    [Fact]
    public async Task RunProjectImportAsync_CancellationMarksProjectCanceled()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("cancel");
        workspace.WriteFiles(sourceFolder, 600);

        var store = new PicSelectStore(workspace.DatabasePath);
        var createdProject = await store.CreateProjectAsync(sourceFolder);
        using var cancellationTokenSource = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(() => store.RunProjectImportAsync(
            createdProject.ProjectId,
            new CancelAfterFirstImportingProgress(cancellationTokenSource),
            cancellationTokenSource.Token));

        var project = await store.GetProjectOverviewAsync(createdProject.ProjectId);

        Assert.NotNull(project);
        Assert.Equal(ProjectImportStatus.Canceled, project.ImportStatus);
        Assert.True(project.Iterations.Single().TotalPhotoCount > 0);
    }

    [Fact]
    public async Task MarkIncompleteImportsInterrupted_UpdatesInFlightProjects()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("interrupt");

        var store = new PicSelectStore(workspace.DatabasePath);
        var createdProject = await store.CreateProjectAsync(sourceFolder);
        await store.SetProjectImportStatusAsync(createdProject.ProjectId, ProjectImportStatus.Importing);

        store.MarkIncompleteImportsInterrupted();

        var project = await store.GetProjectOverviewAsync(createdProject.ProjectId);

        Assert.NotNull(project);
        Assert.Equal(ProjectImportStatus.Interrupted, project.ImportStatus);
    }

    [Fact]
    public async Task ResetProjectImportAsync_ClearsPartialSnapshotAndAllowsFreshRestart()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("restart");
        workspace.WriteFiles(sourceFolder, 600);

        var store = new PicSelectStore(workspace.DatabasePath);
        var createdProject = await store.CreateProjectAsync(sourceFolder);
        using var cancellationTokenSource = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(() => store.RunProjectImportAsync(
            createdProject.ProjectId,
            new CancelAfterFirstImportingProgress(cancellationTokenSource),
            cancellationTokenSource.Token));

        await store.ResetProjectImportAsync(createdProject.ProjectId);

        var resetProject = await store.GetProjectOverviewAsync(createdProject.ProjectId);
        Assert.NotNull(resetProject);
        Assert.Equal(ProjectImportStatus.Pending, resetProject.ImportStatus);
        Assert.Empty(resetProject.Iterations);

        var importedProject = await store.RunProjectImportAsync(createdProject.ProjectId);
        var completedProject = await store.GetProjectOverviewAsync(createdProject.ProjectId);

        Assert.Equal(600, importedProject.ImportedPhotoCount);
        Assert.NotNull(completedProject);
        Assert.Equal(ProjectImportStatus.Completed, completedProject.ImportStatus);
        Assert.Equal(600, completedProject.Iterations.Single().TotalPhotoCount);
    }

    [Fact]
    public async Task DeleteProjectAsync_RemovesProjectFromCatalog()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("delete");
        workspace.WriteFiles(sourceFolder, 2);

        var store = new PicSelectStore(workspace.DatabasePath);
        var importedProject = await store.ImportProjectFromFolderAsync(sourceFolder);

        await store.DeleteProjectAsync(importedProject.ProjectId);

        Assert.Empty(await store.GetProjectsAsync());
        Assert.Null(await store.GetProjectOverviewAsync(importedProject.ProjectId));
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

        public void WriteFiles(string folderPath, int count)
        {
            for (var index = 0; index < count; index++)
            {
                File.WriteAllText(Path.Combine(folderPath, $"frame-{index:0000}.jpg"), "test");
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private sealed class CancelAfterFirstImportingProgress(CancellationTokenSource cancellationTokenSource) : IProgress<ProjectImportProgress>
    {
        private bool hasCanceled;

        public void Report(ProjectImportProgress value)
        {
            if (hasCanceled || value.ImportStatus != ProjectImportStatus.Importing)
            {
                return;
            }

            hasCanceled = true;
            cancellationTokenSource.Cancel();
        }
    }
}
