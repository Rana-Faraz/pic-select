using PicSelect.Core.Projects;

namespace PicSelect.Core.Tests;

public sealed class HistoricalEditTests
{
    [Fact]
    public async Task RecordDecisionAsync_CanChangeDecisionInCompletedIteration()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("history");
        workspace.WriteFile(Path.Combine(sourceFolder, "a.jpg"));
        workspace.WriteFile(Path.Combine(sourceFolder, "b.jpg"));

        var store = new PicSelectStore(workspace.DatabasePath);
        var importedProject = await store.ImportProjectFromFolderAsync(sourceFolder);
        var session = await store.GetReviewSessionAsync(importedProject.ProjectId, 1);

        Assert.NotNull(session);
        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session.Photos[0].PhotoId, "choose");
        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session.Photos[1].PhotoId, "ignore");
        _ = await store.CreateNextIterationAsync(importedProject.ProjectId, 1);

        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session.Photos[1].PhotoId, "choose");

        var updatedIteration = await store.GetIterationPhotosAsync(importedProject.ProjectId, 1);
        Assert.Equal(new[] { "choose", "choose" }, updatedIteration.Select(photo => photo.DecisionType));
    }

    [Fact]
    public async Task PromotePhotoToIterationAsync_BackfillsMissingIntermediateIterations()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("promote");
        workspace.WriteFile(Path.Combine(sourceFolder, "a.jpg"));
        workspace.WriteFile(Path.Combine(sourceFolder, "b.jpg"));
        workspace.WriteFile(Path.Combine(sourceFolder, "c.jpg"));

        var store = new PicSelectStore(workspace.DatabasePath);
        var importedProject = await store.ImportProjectFromFolderAsync(sourceFolder);
        var session1 = await store.GetReviewSessionAsync(importedProject.ProjectId, 1);

        Assert.NotNull(session1);
        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session1.Photos[0].PhotoId, "choose");
        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session1.Photos[1].PhotoId, "choose");
        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session1.Photos[2].PhotoId, "ignore");
        _ = await store.CreateNextIterationAsync(importedProject.ProjectId, 1);

        var session2 = await store.GetReviewSessionAsync(importedProject.ProjectId, 2);
        Assert.NotNull(session2);
        await store.RecordDecisionAsync(importedProject.ProjectId, 2, session2.Photos[0].PhotoId, "choose");
        await store.RecordDecisionAsync(importedProject.ProjectId, 2, session2.Photos[1].PhotoId, "ignore");
        _ = await store.CreateNextIterationAsync(importedProject.ProjectId, 2);

        await store.PromotePhotoToIterationAsync(importedProject.ProjectId, session1.Photos[2].PhotoId, 3);

        var iteration2Photos = await store.GetIterationPhotosAsync(importedProject.ProjectId, 2);
        var iteration3Photos = await store.GetIterationPhotosAsync(importedProject.ProjectId, 3);

        Assert.Contains(iteration2Photos, photo => photo.FileName == "c.jpg");
        Assert.Contains(iteration3Photos, photo => photo.FileName == "c.jpg");
    }

    [Fact]
    public async Task RemovePhotoFromIterationAsync_RemovesPhotoFromSelectedAndLaterIterations()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("remove");
        workspace.WriteFile(Path.Combine(sourceFolder, "a.jpg"));
        workspace.WriteFile(Path.Combine(sourceFolder, "b.jpg"));

        var store = new PicSelectStore(workspace.DatabasePath);
        var importedProject = await store.ImportProjectFromFolderAsync(sourceFolder);
        var session1 = await store.GetReviewSessionAsync(importedProject.ProjectId, 1);

        Assert.NotNull(session1);
        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session1.Photos[0].PhotoId, "choose");
        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session1.Photos[1].PhotoId, "choose");
        _ = await store.CreateNextIterationAsync(importedProject.ProjectId, 1);

        var session2 = await store.GetReviewSessionAsync(importedProject.ProjectId, 2);
        Assert.NotNull(session2);
        await store.RecordDecisionAsync(importedProject.ProjectId, 2, session2.Photos[0].PhotoId, "choose");
        await store.RecordDecisionAsync(importedProject.ProjectId, 2, session2.Photos[1].PhotoId, "choose");
        _ = await store.CreateNextIterationAsync(importedProject.ProjectId, 2);

        await store.RemovePhotoFromIterationAsync(importedProject.ProjectId, session1.Photos[0].PhotoId, 2);

        var iteration2Photos = await store.GetIterationPhotosAsync(importedProject.ProjectId, 2);
        var iteration3Photos = await store.GetIterationPhotosAsync(importedProject.ProjectId, 3);

        Assert.DoesNotContain(iteration2Photos, photo => photo.FileName == "a.jpg");
        Assert.DoesNotContain(iteration3Photos, photo => photo.FileName == "a.jpg");
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
