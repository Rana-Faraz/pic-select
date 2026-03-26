using PicSelect.Core.Projects;

namespace PicSelect.Services;

public sealed class ProjectImportCoordinator
{
    private readonly object gate = new();
    private readonly Dictionary<long, Task> activeImports = new();
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
        lock (gate)
        {
            if (activeImports.ContainsKey(projectId))
            {
                return;
            }

            activeImports[projectId] = RunImportAsync(projectId);
        }
    }

    private async Task RunImportAsync(long projectId)
    {
        try
        {
            await store.RunProjectImportAsync(projectId);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Project import failed for {projectId}: {exception}");
        }
        finally
        {
            lock (gate)
            {
                activeImports.Remove(projectId);
            }
        }
    }
}
