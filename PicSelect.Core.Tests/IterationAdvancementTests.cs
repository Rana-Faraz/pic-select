using PicSelect.Core.Projects;

namespace PicSelect.Core.Tests;

public sealed class IterationAdvancementTests
{
    [Fact]
    public async Task CreateNextIterationAsync_UsesChosenPhotosOnly()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("advance");
        workspace.WriteFile(Path.Combine(sourceFolder, "a.jpg"));
        workspace.WriteFile(Path.Combine(sourceFolder, "b.jpg"));
        workspace.WriteFile(Path.Combine(sourceFolder, "c.jpg"));

        var store = new PicSelectStore(workspace.DatabasePath);
        var importedProject = await store.ImportProjectFromFolderAsync(sourceFolder);
        var session = await store.GetReviewSessionAsync(importedProject.ProjectId, 1);

        Assert.NotNull(session);

        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session.Photos[0].PhotoId, "choose");
        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session.Photos[1].PhotoId, "ignore");
        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session.Photos[2].PhotoId, "choose");

        var nextIteration = await store.CreateNextIterationAsync(importedProject.ProjectId, 1);
        var nextPhotos = await store.GetIterationPhotosAsync(importedProject.ProjectId, 2);

        Assert.NotNull(nextIteration);
        Assert.Equal(2, nextIteration.Number);
        Assert.Equal(new[] { "a.jpg", "c.jpg" }, nextPhotos.Select(photo => photo.FileName));
    }

    [Fact]
    public async Task CreateNextIterationAsync_RequiresCurrentIterationToBeComplete()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("incomplete");
        workspace.WriteFile(Path.Combine(sourceFolder, "a.jpg"));
        workspace.WriteFile(Path.Combine(sourceFolder, "b.jpg"));

        var store = new PicSelectStore(workspace.DatabasePath);
        var importedProject = await store.ImportProjectFromFolderAsync(sourceFolder);
        var session = await store.GetReviewSessionAsync(importedProject.ProjectId, 1);

        Assert.NotNull(session);
        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session.CurrentPhoto.PhotoId, "choose");

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateNextIterationAsync(importedProject.ProjectId, 1));
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
