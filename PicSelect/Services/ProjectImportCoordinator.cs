using PicSelect.Core.Projects;

namespace PicSelect.Services;

public sealed class ProjectImportCoordinator
{
    private readonly object gate = new();
    private readonly Dictionary<long, ActiveImport> activeImports = new();
    private readonly PicSelectStore store;

    public ProjectImportCoordinator(PicSelectStore store)
    {
        this.store = store;
    }

    public bool HasActiveImports
    {
        get
        {
            lock (gate)
            {
                return activeImports.Count > 0;
            }
        }
    }

    public bool IsImportActive(long projectId)
    {
        lock (gate)
        {
            return activeImports.ContainsKey(projectId);
        }
    }

    public void StartImport(long projectId)
    {
        CancellationTokenSource cancellationTokenSource;
        Task importTask;

        lock (gate)
        {
            if (activeImports.ContainsKey(projectId))
            {
                return;
            }

            cancellationTokenSource = new CancellationTokenSource();
            importTask = RunImportAsync(projectId, cancellationTokenSource);
            activeImports[projectId] = new ActiveImport(cancellationTokenSource, importTask);
        }
    }

    public async Task CancelImportAsync(long projectId)
    {
        ActiveImport? activeImport;
        lock (gate)
        {
            activeImports.TryGetValue(projectId, out activeImport);
        }

        if (activeImport is null)
        {
            return;
        }

        activeImport.CancellationTokenSource.Cancel();

        try
        {
            await activeImport.ImportTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunImportAsync(long projectId, CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            await store.RunProjectImportAsync(projectId, cancellationToken: cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Project import failed for {projectId}: {exception}");
        }
        finally
        {
            cancellationTokenSource.Dispose();
            lock (gate)
            {
                activeImports.Remove(projectId);
            }
        }
    }

    private sealed record ActiveImport(CancellationTokenSource CancellationTokenSource, Task ImportTask);
}
