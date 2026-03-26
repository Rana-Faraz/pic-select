using Microsoft.UI.Xaml.Navigation;
using PicSelect.Core.Projects;
using PicSelect.Navigation;

namespace PicSelect.Views;

public sealed partial class ProjectDetailPage : Page
{
    private readonly PicSelectStore store;
    private long currentProjectId;

    public ProjectDetailPage()
    {
        InitializeComponent();
        store = ((App)Application.Current).Store;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (!TryGetProjectId(e.Parameter, out var projectId))
        {
            ShowMissingProject();
            return;
        }

        var project = await store.GetProjectOverviewAsync(projectId);
        if (project is null)
        {
            ShowMissingProject();
            return;
        }

        currentProjectId = project.ProjectId;
        MissingProjectBorder.Visibility = Visibility.Collapsed;
        IterationsListView.Visibility = Visibility.Visible;
        ProjectNameTextBlock.Text = project.DisplayName;
        FolderPathTextBlock.Text = project.FolderPath;
        IterationsListView.ItemsSource = project.Iterations;
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
        ProjectNameTextBlock.Text = "Project unavailable";
        FolderPathTextBlock.Text = string.Empty;
        IterationsListView.ItemsSource = null;
        IterationsListView.Visibility = Visibility.Collapsed;
        MissingProjectBorder.Visibility = Visibility.Visible;
    }

    private void OnIterationInvoked(object sender, ItemClickEventArgs e)
    {
        if (currentProjectId == 0 || e.ClickedItem is not IterationSummary iteration)
        {
            return;
        }

        if (iteration.TotalPhotoCount > 0 && iteration.ReviewedPhotoCount == iteration.TotalPhotoCount)
        {
            Frame.Navigate(typeof(IterationGalleryPage), new ProjectIterationNavigationArgs(currentProjectId, iteration.Number));
            return;
        }

        Frame.Navigate(typeof(ReviewPage), new ReviewNavigationArgs(currentProjectId, iteration.Number));
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
}
