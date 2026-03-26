using Microsoft.UI.Xaml.Navigation;
using PicSelect.Core.Projects;
using PicSelect.Navigation;

namespace PicSelect.Views;

public sealed partial class IterationSummaryPage : Page
{
    private readonly PicSelectStore store;
    private long projectId;
    private int iterationNumber;
    private IterationSummary? iterationSummary;

    public IterationSummaryPage()
    {
        InitializeComponent();
        store = ((App)Application.Current).Store;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not ProjectIterationNavigationArgs args)
        {
            return;
        }

        projectId = args.ProjectId;
        iterationNumber = args.IterationNumber;

        var project = await store.GetProjectOverviewAsync(projectId);
        iterationSummary = project?.Iterations.SingleOrDefault(item => item.Number == iterationNumber);
        if (iterationSummary is null)
        {
            return;
        }

        HeaderTextBlock.Text = $"Iteration {iterationSummary.Number} complete";
        TotalTextBlock.Text = $"Total: {iterationSummary.TotalPhotoCount}";
        ChosenTextBlock.Text = $"Chosen: {iterationSummary.ChosenPhotoCount}";
        IgnoredTextBlock.Text = $"Ignored: {iterationSummary.IgnoredPhotoCount}";
    }

    private async void OnStartNextIterationClicked(object sender, RoutedEventArgs e)
    {
        var nextIteration = await store.CreateNextIterationAsync(projectId, iterationNumber);
        if (nextIteration.TotalPhotoCount > 0)
        {
            Frame.Navigate(typeof(ReviewPage), new ReviewNavigationArgs(projectId, nextIteration.Number));
            return;
        }

        Frame.Navigate(typeof(ProjectDetailPage), projectId);
    }

    private void OnReturnToProjectClicked(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(ProjectDetailPage), projectId);
    }
}
