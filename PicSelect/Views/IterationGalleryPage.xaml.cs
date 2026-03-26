using Microsoft.UI.Xaml.Navigation;
using PicSelect.Core.Projects;
using PicSelect.Navigation;

namespace PicSelect.Views;

public sealed partial class IterationGalleryPage : Page
{
    private readonly PicSelectStore store;
    private long projectId;
    private int iterationNumber;

    public IterationGalleryPage()
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
        HeaderTextBlock.Text = $"Iteration {iterationNumber} gallery";

        var photos = await store.GetIterationPhotosAsync(projectId, iterationNumber);
        GalleryGridView.ItemsSource = photos
            .Select(photo => new GalleryPhotoItem(
                photo.PhotoId,
                photo.FileName,
                photo.DecisionType is null ? "Undecided" : photo.DecisionType.ToUpperInvariant(),
                new Uri(photo.FilePath)))
            .ToList();
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void OnPhotoInvoked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GalleryPhotoItem photo)
        {
            return;
        }

        Frame.Navigate(typeof(ReviewPage), new ReviewNavigationArgs(projectId, iterationNumber, photo.PhotoId));
    }

    private sealed record GalleryPhotoItem(long PhotoId, string FileName, string DecisionLabel, Uri PreviewUri);
}
