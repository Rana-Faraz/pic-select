using PicSelect.Core.Projects;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PicSelect.Views
{
    public sealed partial class MainPage : Page
    {
        private readonly PicSelectStore store;

        public MainPage()
        {
            InitializeComponent();
            store = ((App)Application.Current).Store;
            Loaded += OnLoaded;
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

                var importedProject = await store.ImportProjectFromFolderAsync(folderPath);
                await LoadProjectsAsync();

                StatusTextBlock.Text = importedProject.AlreadyExisted
                    ? $"Project already exists for '{importedProject.FolderPath}'."
                    : $"Imported {importedProject.ImportedPhotoCount} photos from '{importedProject.FolderPath}'.";

                OpenProject(importedProject.ProjectId);
            }
            catch (Exception exception)
            {
                StatusTextBlock.Text = $"Import failed: {exception.Message}";
            }
            finally
            {
                ImportProgressRing.IsActive = false;
                CreateProjectButton.IsEnabled = true;
            }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
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
            var projects = await store.GetProjectsAsync();
            ProjectsListView.ItemsSource = projects;
            ProjectsListView.Visibility = projects.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyStateBorder.Visibility = projects.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OpenProject(long projectId)
        {
            Frame.Navigate(typeof(ProjectDetailPage), projectId);
        }
    }
}
