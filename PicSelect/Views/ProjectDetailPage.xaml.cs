using Microsoft.UI.Xaml.Navigation;
using PicSelect.Core.Projects;
using PicSelect.Navigation;
using PicSelect.Services;
using Microsoft.UI.Dispatching;

namespace PicSelect.Views;

public sealed partial class ProjectDetailPage : Page
{
    private readonly ProjectImportCoordinator importCoordinator;
    private readonly DispatcherQueueTimer refreshTimer;
    private readonly PicSelectStore store;
    private long currentProjectId;
    private ProjectImportStatus currentImportStatus;
    private bool isRefreshingProject;

    public ProjectDetailPage()
    {
        InitializeComponent();
        var app = (App)Application.Current;
        store = app.Store;
        importCoordinator = app.ImportCoordinator;
        refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        refreshTimer.Interval = TimeSpan.FromSeconds(1);
        refreshTimer.Tick += OnRefreshTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (!TryGetProjectId(e.Parameter, out var projectId))
        {
            ShowMissingProject();
            return;
        }

        await LoadProjectAsync(projectId);
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void ShowMissingProject()
    {
        currentProjectId = 0;
        currentImportStatus = ProjectImportStatus.Failed;
        ProjectNameTextBlock.Text = "Project unavailable";
        FolderPathTextBlock.Text = string.Empty;
        ImportStatusTextBlock.Text = string.Empty;
        ImportHintTextBlock.Text = string.Empty;
        ImportProgressTextBlock.Text = string.Empty;
        ImportElapsedTextBlock.Text = string.Empty;
        ImportStateBorder.Visibility = Visibility.Collapsed;
        IterationsListView.ItemsSource = null;
        IterationsListView.Visibility = Visibility.Collapsed;
        IterationsListView.IsItemClickEnabled = false;
        MissingProjectBorder.Visibility = Visibility.Visible;
    }

    private void OnIterationInvoked(object sender, ItemClickEventArgs e)
    {
        if (currentProjectId == 0 || e.ClickedItem is not IterationSummary iteration)
        {
            return;
        }

        if (currentImportStatus != ProjectImportStatus.Completed)
        {
            ImportHintTextBlock.Text = GetImportHint(currentImportStatus);
            ImportStateBorder.Visibility = Visibility.Visible;
            return;
        }

        if (iteration.TotalPhotoCount > 0 && iteration.ReviewedPhotoCount == iteration.TotalPhotoCount)
        {
            Frame.Navigate(typeof(IterationGalleryPage), new ProjectIterationNavigationArgs(currentProjectId, iteration.Number));
            return;
        }

        Frame.Navigate(typeof(ReviewPage), new ReviewNavigationArgs(currentProjectId, iteration.Number));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        refreshTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        refreshTimer.Stop();
    }

    private async void OnRefreshTick(DispatcherQueueTimer sender, object args)
    {
        if (currentProjectId == 0)
        {
            refreshTimer.Stop();
            return;
        }

        if (currentImportStatus == ProjectImportStatus.Completed && !importCoordinator.IsImportActive(currentProjectId))
        {
            refreshTimer.Stop();
            return;
        }

        await LoadProjectAsync(currentProjectId);
    }

    private static bool TryGetProjectId(object parameter, out long projectId)
    {
        switch (parameter)
        {
            case long longValue:
                projectId = longValue;
                return true;
            case int intValue:
                projectId = intValue;
                return true;
            default:
                projectId = 0;
                return false;
        }
    }

    private async Task LoadProjectAsync(long projectId)
    {
        if (isRefreshingProject)
        {
            return;
        }

        isRefreshingProject = true;
        try
        {
            var project = await store.GetProjectOverviewAsync(projectId);
            if (project is null)
            {
                ShowMissingProject();
                return;
            }

            currentProjectId = project.ProjectId;
            currentImportStatus = project.ImportStatus;
            MissingProjectBorder.Visibility = Visibility.Collapsed;
            ProjectNameTextBlock.Text = project.DisplayName;
            FolderPathTextBlock.Text = project.FolderPath;
            ImportStatusTextBlock.Text = $"Status: {project.ImportStatus}";
            ImportHintTextBlock.Text = GetImportHint(project.ImportStatus);
            ImportProgressTextBlock.Text = GetImportProgressText(project);
            ImportElapsedTextBlock.Text = $"Elapsed {project.ImportElapsedText}";
            ImportStateBorder.Visibility = project.IsReviewAvailable ? Visibility.Collapsed : Visibility.Visible;
            IterationsListView.IsItemClickEnabled = project.IsReviewAvailable;
            IterationsListView.Visibility = project.Iterations.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            IterationsListView.ItemsSource = project.Iterations;

            if (!project.IsReviewAvailable || importCoordinator.IsImportActive(projectId))
            {
                refreshTimer.Start();
            }
        }
        finally
        {
            isRefreshingProject = false;
        }
    }

    private static string GetImportProgressText(ProjectOverview project)
    {
        var importedPhotoCount = project.Iterations.FirstOrDefault()?.TotalPhotoCount ?? 0;
        return project.ImportStatus == ProjectImportStatus.Completed
            ? $"{importedPhotoCount} photos imported and ready for review."
            : $"{importedPhotoCount} photos imported so far.";
    }

    private static string GetImportHint(ProjectImportStatus importStatus) =>
        importStatus switch
        {
            ProjectImportStatus.Pending => "Snapshot import has not started yet. Review stays locked until the project is fully imported.",
            ProjectImportStatus.Scanning => "PicSelect is still scanning the folder. Review stays locked until the snapshot completes.",
            ProjectImportStatus.Importing => "PicSelect is still importing photos. Review stays locked until the snapshot completes.",
            ProjectImportStatus.Canceled => "This import was canceled. Restart the import before reviewing any iteration.",
            ProjectImportStatus.Interrupted => "This import was interrupted. Restart the import before reviewing any iteration.",
            ProjectImportStatus.Failed => "This import failed. Fix the problem and restart the import before reviewing any iteration.",
            _ => "Snapshot complete. You can open an iteration to review photos."
        };
}
