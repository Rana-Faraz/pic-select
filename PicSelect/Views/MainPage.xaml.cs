using PicSelect.Core.Projects;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PicSelect.Views
{
    public partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
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

                var store = ((App)Application.Current).Store;
                var importedProject = await store.ImportProjectFromFolderAsync(folderPath);

                StatusTextBlock.Text = importedProject.AlreadyExisted
                    ? $"Project already exists for '{importedProject.FolderPath}'."
                    : $"Imported {importedProject.ImportedPhotoCount} photos from '{importedProject.FolderPath}'.";
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

        private static async Task<string?> PickFolderAsync()
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");

            var app = (App)Application.Current;
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(app.MainWindow));

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
    }
}
