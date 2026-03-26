using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using PicSelect.Core.Projects;
using PicSelect.Services;
using WinRT.Interop;

namespace PicSelect
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PicSelect");

            Store = new PicSelectStore(Path.Combine(appDataPath, "picselect.db"));
            Store.MarkIncompleteImportsInterrupted();
            ThumbnailCache = new ThumbnailCacheService(Store, Path.Combine(appDataPath, "thumbnail-cache"));
            ImportCoordinator = new ProjectImportCoordinator(Store, ThumbnailCache);
        }

        public Window MainWindow => window ?? throw new InvalidOperationException("Main window has not been created yet.");

        public PicSelectStore Store { get; }

        public ThumbnailCacheService ThumbnailCache { get; }

        public ProjectImportCoordinator ImportCoordinator { get; }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            window ??= new Window();

            if (window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                window.Content = rootFrame;
            }

            ConfigureWindow(window, rootFrame);
            _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
            window.Activate();
        }

        private static void ConfigureWindow(Window appWindow, Frame rootFrame)
        {
            appWindow.Title = "PicSelect";
            rootFrame.RequestedTheme = ElementTheme.Dark;

            appWindow.SystemBackdrop = new MicaBackdrop();

            var hwnd = WindowNative.GetWindowHandle(appWindow);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var nativeWindow = AppWindow.GetFromWindowId(windowId);

            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            var titleBar = nativeWindow.TitleBar;
            var activeBackground = ColorHelper.FromArgb(255, 18, 20, 24);
            var inactiveBackground = ColorHelper.FromArgb(255, 28, 31, 36);
            var hoverBackground = ColorHelper.FromArgb(255, 42, 45, 52);
            var pressedBackground = ColorHelper.FromArgb(255, 58, 62, 70);
            var captionForeground = Colors.White;
            var inactiveForeground = ColorHelper.FromArgb(255, 176, 180, 186);

            titleBar.BackgroundColor = activeBackground;
            titleBar.ForegroundColor = captionForeground;
            titleBar.InactiveBackgroundColor = inactiveBackground;
            titleBar.InactiveForegroundColor = inactiveForeground;
            titleBar.ButtonBackgroundColor = activeBackground;
            titleBar.ButtonForegroundColor = captionForeground;
            titleBar.ButtonHoverBackgroundColor = hoverBackground;
            titleBar.ButtonHoverForegroundColor = captionForeground;
            titleBar.ButtonPressedBackgroundColor = pressedBackground;
            titleBar.ButtonPressedForegroundColor = captionForeground;
            titleBar.ButtonInactiveBackgroundColor = inactiveBackground;
            titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
