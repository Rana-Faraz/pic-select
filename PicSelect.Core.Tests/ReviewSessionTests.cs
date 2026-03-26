using PicSelect.Core.Projects;

namespace PicSelect.Core.Tests;

public sealed class ReviewSessionTests
{
    [Fact]
    public async Task RecordDecisionAsync_PersistsDecisionsInReviewSession()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("review");
        workspace.WriteFile(Path.Combine(sourceFolder, "a.jpg"));
        workspace.WriteFile(Path.Combine(sourceFolder, "b.jpg"));

        var store = new PicSelectStore(workspace.DatabasePath);
        var importedProject = await store.ImportProjectFromFolderAsync(sourceFolder);
        var session = await store.GetReviewSessionAsync(importedProject.ProjectId, 1);

        Assert.NotNull(session);

        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session.CurrentPhoto.PhotoId, "choose");
        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session.Photos[1].PhotoId, "ignore");

        var updatedSession = await store.GetReviewSessionAsync(importedProject.ProjectId, 1);

        Assert.NotNull(updatedSession);
        Assert.Equal(2, updatedSession.ReviewedPhotoCount);
        Assert.Equal(new[] { "choose", "ignore" }, updatedSession.Photos.Select(photo => photo.DecisionType));
    }

    [Fact]
    public async Task GetReviewSessionAsync_ResumesAtNextUndecidedPhotoAfterStoreReopen()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("resume");
        workspace.WriteFile(Path.Combine(sourceFolder, "a.jpg"));
        workspace.WriteFile(Path.Combine(sourceFolder, "b.jpg"));
        workspace.WriteFile(Path.Combine(sourceFolder, "c.jpg"));

        var firstStore = new PicSelectStore(workspace.DatabasePath);
        var importedProject = await firstStore.ImportProjectFromFolderAsync(sourceFolder);
        var firstSession = await firstStore.GetReviewSessionAsync(importedProject.ProjectId, 1);

        Assert.NotNull(firstSession);
        await firstStore.RecordDecisionAsync(importedProject.ProjectId, 1, firstSession.CurrentPhoto.PhotoId, "choose");

        var secondStore = new PicSelectStore(workspace.DatabasePath);
        var resumedSession = await secondStore.GetReviewSessionAsync(importedProject.ProjectId, 1);

        Assert.NotNull(resumedSession);
        Assert.Equal("b.jpg", resumedSession.CurrentPhoto.FileName);
        Assert.Equal(1, resumedSession.ReviewedPhotoCount);
    }

    [Fact]
    public async Task UndoLastDecisionAsync_RestoresPreviousEffectiveDecision()
    {
        using var workspace = new TestWorkspace();
        var sourceFolder = workspace.CreateDirectory("undo");
        workspace.WriteFile(Path.Combine(sourceFolder, "a.jpg"));
        workspace.WriteFile(Path.Combine(sourceFolder, "b.jpg"));

        var store = new PicSelectStore(workspace.DatabasePath);
        var importedProject = await store.ImportProjectFromFolderAsync(sourceFolder);
        var session = await store.GetReviewSessionAsync(importedProject.ProjectId, 1);

        Assert.NotNull(session);

        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session.CurrentPhoto.PhotoId, "choose");
        await store.RecordDecisionAsync(importedProject.ProjectId, 1, session.CurrentPhoto.PhotoId, "ignore");

        var undonePhotoId = await store.UndoLastDecisionAsync(importedProject.ProjectId, 1);
        var updatedSession = await store.GetReviewSessionAsync(importedProject.ProjectId, 1, undonePhotoId);

        Assert.NotNull(updatedSession);
        Assert.Equal(session.CurrentPhoto.PhotoId, undonePhotoId);
        Assert.Equal("choose", updatedSession.CurrentPhoto.DecisionType);
        Assert.Equal(1, updatedSession.ReviewedPhotoCount);
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
