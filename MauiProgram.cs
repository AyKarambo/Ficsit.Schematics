using Ficsit.Schematics.Core.GameData.Catalog;
using Ficsit.Schematics.Data;
using Ficsit.Schematics.Services;
using Ficsit.Schematics.ViewModels;
using Microsoft.Extensions.Logging;

namespace Ficsit.Schematics
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            builder.Services.AddSingleton(_ => GameDataCatalog.BuildDatabase());
            builder.Services.AddSingleton(_ => new FicsitStore(
                Path.Combine(FileSystem.AppDataDirectory, "ficsit.schematics.db")));
            builder.Services.AddSingleton<AppState>();
            builder.Services.AddSingleton<IconStore>();
            builder.Services.AddSingleton<LocalizationService>();
            builder.Services.AddSingleton<NumberFormatService>();
            builder.Services.AddSingleton<RecipeChooserViewModel>();
            builder.Services.AddSingleton<SummaryViewModel>();
            builder.Services.AddSingleton<MainPage>();

            return builder.Build();
        }
    }
}
