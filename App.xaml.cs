using Ficsit.Schematics.Services;

namespace Ficsit.Schematics
{
    public partial class App : Application
    {
        private readonly AppState _state;
        private readonly MainPage _mainPage;

        public App(AppState state, MainPage mainPage)
        {
            InitializeComponent();
            _state = state;
            _mainPage = mainPage;
            UserAppTheme = state.Settings.DarkMode ? AppTheme.Dark : AppTheme.Light;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var settings = _state.Settings;
            var window = new Window(_mainPage)
            {
                Title = "Ficsit Schematics",
                Width = Math.Max(900, settings.WindowWidth),
                Height = Math.Max(600, settings.WindowHeight),
            };
            if (settings.WindowX > 0 || settings.WindowY > 0)
            {
                window.X = settings.WindowX;
                window.Y = settings.WindowY;
            }

            window.Destroying += (_, _) =>
            {
                settings.WindowWidth = window.Width;
                settings.WindowHeight = window.Height;
                settings.WindowX = window.X;
                settings.WindowY = window.Y;
                _state.SaveSettings();
                _state.SaveNow();
                _state.Backup();
            };
            return window;
        }
    }
}
