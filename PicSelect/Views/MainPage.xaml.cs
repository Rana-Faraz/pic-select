using PicSelect.Core.Projects;
using PicSelect.Services;
using Microsoft.UI.Dispatching;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PicSelect.Views
{
    public sealed partial class MainPage : Page
    {
        private readonly ProjectImportCoordinator importCoordinator;
        private readonly DispatcherQueueTimer refreshTimer;
        private readonly PicSelectStore store;
        private bool isRefreshingProjects;

        public MainPage()
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

        private async void OnCreateProjectClicked(object sender, RoutedEventArgs e)
        {
            CreateProjectButton.IsEnabled = false;
            ImportProgressRing.IsActive = true;

            try
            {
                var folderPath = await PickFolderAsync();
                if (folderPath is null)
                {
                    StatusTextBlock.Text = "Folder import cancelled.";
                    return;
                }

                var createdProject = await store.CreateProjectAsync(folderPath);
                await LoadProjectsAsync();

                if (createdProject.ImportStatus == ProjectImportStatus.Completed)
                {
                    StatusTextBlock.Text = $"Project already exists for '{createdProject.FolderPath}'.";
                    OpenProject(createdProject.ProjectId);
                    return;
                }

                if (createdProject.AlreadyExisted &&
                    (createdProject.ImportStatus == ProjectImportStatus.Canceled ||
                     createdProject.ImportStatus == ProjectImportStatus.Interrupted ||
                     createdProject.ImportStatus == ProjectImportStatus.Failed))
                {
                    StatusTextBlock.Text = $"Project '{createdProject.FolderPath}' is incomplete. Open it and choose Restart Import.";
                    OpenProject(createdProject.ProjectId);
                    return;
                }

                importCoordinator.StartImport(createdProject.ProjectId);
                refreshTimer.Start();

                StatusTextBlock.Text = createdProject.AlreadyExisted
                    ? $"Resuming the background import for '{createdProject.FolderPath}'."
                    : $"Created project for '{createdProject.FolderPath}'. Importing in the background now.";

                OpenProject(createdProject.ProjectId);
            }
            catch (Exception exception)
            {
                StatusTextBlock.Text = $"Import failed: {exception.Message}";
                await LoadProjectsAsync();
            }
            finally
            {
                ImportProgressRing.IsActive = false;
                CreateProjectButton.IsEnabled = true;
            }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await LoadProjectsAsync();
            refreshTimer.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            refreshTimer.Stop();
        }

        private async void OnRefreshTick(DispatcherQueueTimer sender, object args)
        {
            if (!importCoordinator.HasActiveImports)
            {
                refreshTimer.Stop();
                return;
            }

            await LoadProjectsAsync();
        }

        private void OnProjectInvoked(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ProjectSummary project)
            {
                OpenProject(project.ProjectId);
            }
        }

        private static async Task<string?> PickFolderAsync()
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");

            var app = (App)Application.Current;
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(app.MainWindow));

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }

        private async Task LoadProjectsAsync()
        {
            if (isRefreshingProjects)
            {
                return;
            }

            isRefreshingProjects = true;
            try
            {
            var projects = await store.GetProjectsAsync();
            ProjectsListView.ItemsSource = projects;
            ProjectsListView.Visibility = projects.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyStateBorder.Visibility = projects.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            finally
            {
                isRefreshingProjects = false;
            }
        }

        private void OpenProject(long projectId)
        {
            Frame.Navigate(typeof(ProjectDetailPage), projectId);
        }
    }
}
