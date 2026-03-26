using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using PicSelect.Core.Projects;
using PicSelect.Navigation;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PicSelect.Views;

public sealed partial class ReviewPage : Page
{
    private readonly PicSelectStore store;
    private bool isPanning;
    private double panStartHorizontalOffset;
    private Point panStartPointerPosition;
    private double panStartVerticalOffset;
    private ReviewSession? session;
    private long projectId;
    private int iterationNumber;

    public ReviewPage()
    {
        InitializeComponent();
        store = ((App)Application.Current).Store;
        PhotoScrollViewer.SizeChanged += OnPhotoViewportSizeChanged;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not ReviewNavigationArgs args)
        {
            return;
        }

        projectId = args.ProjectId;
        iterationNumber = args.IterationNumber;
        await LoadSessionAsync(args.PreferredPhotoId);
    }

    private async void OnChooseClicked(object sender, RoutedEventArgs e)
    {
        await ApplyDecisionAsync("choose");
    }

    private async void OnIgnoreClicked(object sender, RoutedEventArgs e)
    {
        await ApplyDecisionAsync("ignore");
    }

    private async void OnPreviousClicked(object sender, RoutedEventArgs e)
    {
        if (session is null || session.CurrentPhotoIndex == 0)
        {
            return;
        }

        await LoadSessionAsync(session.Photos[session.CurrentPhotoIndex - 1].PhotoId);
    }

    private async void OnNextClicked(object sender, RoutedEventArgs e)
    {
        if (session is null || session.CurrentPhotoIndex >= session.Photos.Count - 1)
        {
            return;
        }

        await LoadSessionAsync(session.Photos[session.CurrentPhotoIndex + 1].PhotoId);
    }

    private async void OnUndoClicked(object sender, RoutedEventArgs e)
    {
        var undonePhotoId = await store.UndoLastDecisionAsync(projectId, iterationNumber);
        await LoadSessionAsync(undonePhotoId);
    }

    private async void OnFinishIterationClicked(object sender, RoutedEventArgs e)
    {
        if (session is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Finish iteration early?",
            Content = "All remaining undecided photos will be marked as ignored and you will move to the iteration summary.",
            PrimaryButtonText = "Finish Iteration",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await store.CompleteIterationAsync(projectId, iterationNumber);
        Frame.Navigate(typeof(IterationSummaryPage), new ProjectIterationNavigationArgs(projectId, iterationNumber));
    }

    private async void OnPromoteClicked(object sender, RoutedEventArgs e)
    {
        if (session is null)
        {
            return;
        }

        var targetIteration = await SelectLaterIterationAsync(includeCurrentMembership: false, "Promote to later iteration", "Promote");
        if (targetIteration is null)
        {
            return;
        }

        await store.PromotePhotoToIterationAsync(projectId, session.CurrentPhoto.PhotoId, targetIteration.Value);
    }

    private async void OnRemoveLaterClicked(object sender, RoutedEventArgs e)
    {
        if (session is null)
        {
            return;
        }

        var targetIteration = await SelectLaterIterationAsync(includeCurrentMembership: true, "Remove from later iterations", "Remove");
        if (targetIteration is null)
        {
            return;
        }

        await store.RemovePhotoFromIterationAsync(projectId, session.CurrentPhoto.PhotoId, targetIteration.Value);
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private async Task ApplyDecisionAsync(string decisionType)
    {
        if (session is null)
        {
            return;
        }

        var currentPhoto = session.CurrentPhoto;
        await store.RecordDecisionAsync(projectId, iterationNumber, currentPhoto.PhotoId, decisionType);

        var nextPhotoId = session.CurrentPhotoIndex < session.Photos.Count - 1
            ? session.Photos[session.CurrentPhotoIndex + 1].PhotoId
            : currentPhoto.PhotoId;

        await LoadSessionAsync(nextPhotoId);
        if (session is not null && session.ReviewedPhotoCount == session.Photos.Count)
        {
            Frame.Navigate(typeof(IterationSummaryPage), new ProjectIterationNavigationArgs(projectId, iterationNumber));
        }
    }

    private async Task LoadSessionAsync(long? preferredPhotoId = null)
    {
        session = await store.GetReviewSessionAsync(projectId, iterationNumber, preferredPhotoId);
        if (session is null)
        {
            FileNameTextBlock.Text = "No photos available";
            ProgressTextBlock.Text = string.Empty;
            DecisionTextBlock.Text = string.Empty;
            PhotoImage.Source = null;
            ResetZoom();
            EmptyImageTextBlock.Visibility = Visibility.Visible;
            return;
        }

        FileNameTextBlock.Text = session.CurrentPhoto.FileName;
        ProgressTextBlock.Text = $"{session.ReviewedPhotoCount} of {session.Photos.Count} reviewed";
        DecisionTextBlock.Text = session.CurrentPhoto.DecisionType is null
            ? "Undecided"
            : session.CurrentPhoto.DecisionType.ToUpperInvariant();

        PreviousButton.IsEnabled = session.CurrentPhotoIndex > 0;
        NextButton.IsEnabled = session.CurrentPhotoIndex < session.Photos.Count - 1;

        await LoadImageAsync(session.CurrentPhoto.FilePath);
    }

    private async Task<int?> SelectLaterIterationAsync(bool includeCurrentMembership, string title, string primaryButtonText)
    {
        if (session is null)
        {
            return null;
        }

        var project = await store.GetProjectOverviewAsync(projectId);
        if (project is null)
        {
            return null;
        }

        var laterIterations = new List<int>();
        foreach (var iteration in project.Iterations.Where(item => item.Number > iterationNumber))
        {
            var photos = await store.GetIterationPhotosAsync(projectId, iteration.Number);
            var containsPhoto = photos.Any(photo => photo.PhotoId == session.CurrentPhoto.PhotoId);
            if (containsPhoto == includeCurrentMembership)
            {
                laterIterations.Add(iteration.Number);
            }
        }

        if (laterIterations.Count == 0)
        {
            await ShowMessageAsync(
                includeCurrentMembership ? "Nothing to remove" : "Nothing to promote",
                includeCurrentMembership
                    ? "This photo is not in any later iteration."
                    : "No later iteration is available for promotion.");
            return null;
        }

        var comboBox = new ComboBox
        {
            ItemsSource = laterIterations,
            SelectedIndex = 0,
            MinWidth = 220,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = comboBox,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary && comboBox.SelectedItem is int selectedIteration
            ? selectedIteration
            : null;
    }

    private async Task LoadImageAsync(string filePath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            PhotoImage.Source = image;
            FitImageToViewport();
            ResetZoom();
            EmptyImageTextBlock.Visibility = Visibility.Collapsed;
        }
        catch
        {
            PhotoImage.Source = null;
            ResetZoom();
            EmptyImageTextBlock.Visibility = Visibility.Visible;
            EmptyImageTextBlock.Text = "Unable to load this photo.";
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
        };

        await dialog.ShowAsync();
    }

    private void OnPhotoViewerDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (PhotoImage.Source is null)
        {
            return;
        }

        if (PhotoScrollViewer.ZoomFactor > 1.01f)
        {
            ResetZoom();
            return;
        }

        var targetZoom = 2.5f;
        var position = e.GetPosition(PhotoScrollViewer);
        var targetHorizontalOffset = Math.Max(0, (position.X * targetZoom) - (PhotoScrollViewer.ViewportWidth / 2));
        var targetVerticalOffset = Math.Max(0, (position.Y * targetZoom) - (PhotoScrollViewer.ViewportHeight / 2));
        PhotoScrollViewer.ChangeView(targetHorizontalOffset, targetVerticalOffset, targetZoom);
    }

    private void OnPhotoViewerPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (PhotoImage.Source is null || PhotoScrollViewer.ZoomFactor <= 1.01f)
        {
            return;
        }

        var point = e.GetCurrentPoint(PhotoScrollViewer);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        isPanning = true;
        panStartPointerPosition = point.Position;
        panStartHorizontalOffset = PhotoScrollViewer.HorizontalOffset;
        panStartVerticalOffset = PhotoScrollViewer.VerticalOffset;
        PhotoScrollViewer.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPhotoViewerPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!isPanning || PhotoScrollViewer.ZoomFactor <= 1.01f)
        {
            return;
        }

        var currentPosition = e.GetCurrentPoint(PhotoScrollViewer).Position;
        var deltaX = currentPosition.X - panStartPointerPosition.X;
        var deltaY = currentPosition.Y - panStartPointerPosition.Y;
        var targetHorizontalOffset = Math.Max(0, panStartHorizontalOffset - deltaX);
        var targetVerticalOffset = Math.Max(0, panStartVerticalOffset - deltaY);
        PhotoScrollViewer.ChangeView(targetHorizontalOffset, targetVerticalOffset, null, true);
        e.Handled = true;
    }

    private void OnPhotoViewerPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!isPanning)
        {
            return;
        }

        isPanning = false;
        PhotoScrollViewer.ReleasePointerCaptures();
        e.Handled = true;
    }

    private void OnPhotoViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        FitImageToViewport();
    }

    private void FitImageToViewport()
    {
        if (PhotoImage.Source is null)
        {
            return;
        }

        PhotoImage.MaxWidth = Math.Max(0, PhotoScrollViewer.ActualWidth - 24);
        PhotoImage.MaxHeight = Math.Max(0, PhotoScrollViewer.ActualHeight - 24);
    }

    private void ResetZoom()
    {
        isPanning = false;
        PhotoScrollViewer.ChangeView(0, 0, 1f, true);
    }
}
