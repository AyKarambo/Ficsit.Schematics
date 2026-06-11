using Ficsit.Schematics.Core.Serialization;

namespace Ficsit.Schematics;

/// <summary>.sfmd import/export and the named-saves / backups panel.</summary>
public partial class MainPage
{
    private async void OnImportClicked(object? sender, EventArgs e)
    {
        try
        {
            var sfmd = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.WinUI] = [".sfmd"],
            });
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = _loc.L("IMPORT_FILE"),
                FileTypes = sfmd,
            });
            if (picked is null) return;
            var json = await File.ReadAllTextAsync(picked.FullPath);
            _state.Editor.LoadDocument(SfmdSerializer.Deserialize(json));
            _state.SaveNow();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(_loc.L("ERROR"), ex.Message, "OK");
        }
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
#if WINDOWS
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedFileName = "factory",
            };
            picker.FileTypeChoices.Add(_loc.L("SAVE_FILE_DESCRIPTION"), new List<string> { ".sfmd" });
            var platformWindow = (Microsoft.UI.Xaml.Window?)Window?.Handler?.PlatformView;
            if (platformWindow is null) return;
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(platformWindow));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await File.WriteAllTextAsync(file.Path, SfmdSerializer.Serialize(_state.Editor.Document));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(_loc.L("ERROR"), ex.Message, "OK");
        }
#else
        await Task.CompletedTask;
#endif
    }

    private void RefreshSavesPanel()
    {
        SavesList.Children.Clear();
        foreach (var (name, modified) in _state.Store.ListSaves())
        {
            var row = MakeListRow();
            var grid = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                ],
                ColumnSpacing = 4,
            };
            var text = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center };
            text.Children.Add(new Label { Text = name, FontSize = 12, FontFamily = "OpenSansSemibold", TextColor = RowTextColor() });
            text.Children.Add(new Label { Text = modified.ToLocalTime().ToString("g"), FontSize = 10, TextColor = MutedTextColor() });
            grid.Children.Add(text);

            var load = MakeIconButton("", _loc.L("OPEN"));
            load.Clicked += (_, _) =>
            {
                var doc = _state.Store.LoadNamed(name);
                if (doc is null) return;
                _state.Editor.LoadDocument(doc);
                _state.SaveNow();
                SavesPanel.IsVisible = false;
            };
            Grid.SetColumn(load, 1);
            grid.Children.Add(load);

            var delete = MakeIconButton("", _loc.L("DELETE"), danger: true);
            delete.Clicked += async (_, _) =>
            {
                if (!await DisplayAlertAsync(_loc.L("DELETE"), name, _loc.L("YES"), _loc.L("NO"))) return;
                _state.Store.DeleteNamed(name);
                RefreshSavesPanel();
            };
            Grid.SetColumn(delete, 2);
            grid.Children.Add(delete);

            row.Content = grid;
            SavesList.Children.Add(row);
        }

        BackupsList.Children.Clear();
        foreach (var (id, created) in _state.Store.ListBackups().Take(20))
        {
            var row = MakeListRow();
            var grid = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                ColumnSpacing = 4,
            };
            grid.Children.Add(new Label
            {
                Text = created.ToLocalTime().ToString("g"),
                FontSize = 11.5,
                VerticalOptions = LayoutOptions.Center,
                TextColor = RowTextColor(),
            });

            var restore = MakeIconButton("", _loc.L("OPEN"));
            restore.Clicked += (_, _) =>
            {
                var doc = _state.Store.LoadBackup(id);
                if (doc is null) return;
                _state.Editor.LoadDocument(doc);
                _state.SaveNow();
                SavesPanel.IsVisible = false;
            };
            Grid.SetColumn(restore, 1);
            grid.Children.Add(restore);

            row.Content = grid;
            BackupsList.Children.Add(row);
        }
    }

    private void OnSaveAsClicked(object? sender, EventArgs e)
    {
        var name = SaveNameEntry.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        _state.Store.SaveNamed(name, _state.Editor.Document);
        SaveNameEntry.Text = string.Empty;
        RefreshSavesPanel();
    }

    private Border MakeListRow() => new()
    {
        BackgroundColor = IsDark() ? Color.FromArgb("#272727") : Color.FromArgb("#F4F4F4"),
        StrokeThickness = 0,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
        Padding = new Thickness(8, 5),
    };

    private Button MakeIconButton(string glyph, string tooltip, bool danger = false)
    {
        var button = new Button
        {
            Text = glyph,
            FontFamily = "Segoe MDL2 Assets",
            FontSize = 13,
            WidthRequest = 32,
            HeightRequest = 30,
            Padding = 0,
            BackgroundColor = Colors.Transparent,
            TextColor = danger
                ? (IsDark() ? Color.FromArgb("#FF6B81") : Color.FromArgb("#C62838"))
                : (IsDark() ? Color.FromArgb("#E6E6E6") : Color.FromArgb("#333333")),
            CornerRadius = 7,
        };
        ToolTipProperties.SetText(button, tooltip);
        return button;
    }
}
