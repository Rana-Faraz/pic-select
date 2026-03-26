using Microsoft.UI.Xaml.Navigation;
using PicSelect.Core.Projects;
using PicSelect.Navigation;
using PicSelect.Services;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PicSelect.Views;

public sealed partial class IterationGalleryPage : Page
{
    private readonly DispatcherQueueTimer refreshTimer;
    private readonly PicSelectStore store;
    private readonly ThumbnailCacheService thumbnailCache;
    private readonly ObservableCollection<GalleryPhotoItem> galleryItems = [];
    private long projectId;
    private int iterationNumber;

    public IterationGalleryPage()
    {
        InitializeComponent();
        var app = (App)Application.Current;
        store = app.Store;
        thumbnailCache = app.ThumbnailCache;
        refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        refreshTimer.Interval = TimeSpan.FromSeconds(1);
        refreshTimer.Tick += OnRefreshTick;
        Unloaded += OnUnloaded;
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
        galleryItems.Clear();
        foreach (var photo in photos)
        {
            galleryItems.Add(new GalleryPhotoItem(
                photo.PhotoId,
                photo.FileName,
                photo.DecisionType is null ? "Undecided" : photo.DecisionType.ToUpperInvariant(),
                thumbnailCache.GetThumbnailUri(projectId, photo.PhotoId)));
        }

        GalleryGridView.ItemsSource = galleryItems;
        thumbnailCache.StartThumbnailGeneration(projectId);
        refreshTimer.Start();
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

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        refreshTimer.Stop();
    }

    private void OnRefreshTick(DispatcherQueueTimer sender, object args)
    {
        foreach (var item in galleryItems.Where(item => !item.HasThumbnail))
        {
            item.SetThumbnail(thumbnailCache.GetThumbnailUri(projectId, item.PhotoId));
        }

        if (!thumbnailCache.IsThumbnailingActive(projectId))
        {
            refreshTimer.Stop();
        }
    }

    private sealed class GalleryPhotoItem : INotifyPropertyChanged
    {
        private Uri? thumbnailUri;

        public GalleryPhotoItem(long photoId, string fileName, string decisionLabel, Uri? thumbnailUri)
        {
            PhotoId = photoId;
            FileName = fileName;
            DecisionLabel = decisionLabel;
            this.thumbnailUri = thumbnailUri;
        }

        public long PhotoId { get; }

        public string FileName { get; }

        public string DecisionLabel { get; }

        public Uri? ThumbnailUri
        {
            get => thumbnailUri;
            private set
            {
                if (thumbnailUri == value)
                {
                    return;
                }

                thumbnailUri = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasThumbnail));
                OnPropertyChanged(nameof(ThumbnailVisibility));
                OnPropertyChanged(nameof(PlaceholderVisibility));
            }
        }

        public bool HasThumbnail => ThumbnailUri is not null;

        public Visibility ThumbnailVisibility => HasThumbnail ? Visibility.Visible : Visibility.Collapsed;

        public Visibility PlaceholderVisibility => HasThumbnail ? Visibility.Collapsed : Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetThumbnail(Uri? value)
        {
            if (value is null)
            {
                return;
            }

            ThumbnailUri = value;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
