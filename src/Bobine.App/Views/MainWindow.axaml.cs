using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Bobine.App.ViewModels;
using Bobine.Core.Metadata;

namespace Bobine.App.Views;

public sealed partial class MainWindow : Window, IDialogs
{
    public MainWindow()
    {
        InitializeComponent();
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.F, KeyModifiers.Control),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => SearchBox.Focus()),
        });
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.A, KeyModifiers.Alt),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
            {
                AddButton.Flyout?.ShowAt(AddButton);
                NewTitleBox.Focus();
            }),
        });
    }

    public async Task<bool> EditTitleAsync(TitleEditorViewModel editor)
    {
        var window = new TitleEditorWindow { DataContext = editor };
        return await window.ShowDialog<bool>(this);
    }

    public Task<bool> ConfirmAsync(string title, string message) => MessageWindow.ShowAsync(this, title, message, confirm: true);

    public Task InformAsync(string title, string message) => MessageWindow.ShowAsync(this, title, message, confirm: false);

    public Task<MetadataSearchResult?> SearchMetadataAsync(MetadataSearchViewModel search)
    {
        AddButton.Flyout?.Hide();
        return new MetadataSearchWindow { DataContext = search }.ShowDialog<MetadataSearchResult?>(this);
    }

    public Task<bool> EditSettingsAsync(SettingsViewModel settings) =>
        new SettingsWindow { DataContext = settings }.ShowDialog<bool>(this);

    public Task<bool> AdvancedSearchAsync(AdvancedSearchViewModel search) =>
        new AdvancedSearchWindow { DataContext = search }.ShowDialog<bool>(this);

    public Task<bool> BulkEditAsync(BulkEditViewModel edit) =>
        new BulkEditWindow { DataContext = edit }.ShowDialog<bool>(this);

    public Task<bool> ImportFolderAsync(ImportFolderViewModel import) =>
        new ImportFolderWindow { DataContext = import }.ShowDialog<bool>(this);

    public Task ManageCatalogsAsync(CatalogsViewModel catalogs) =>
        new CatalogsWindow { DataContext = catalogs }.ShowDialog(this);

    public Task<bool> ShowSeriesAsync(SeriesViewModel series) =>
        new SeriesWindow { DataContext = series }.ShowDialog<bool>(this);

    public Task<bool> ShowPersonAsync(PersonViewModel person) =>
        new PersonWindow { DataContext = person }.ShowDialog<bool>(this);

    public Task<bool> ShowPeopleAsync(PeopleBrowserViewModel people) =>
        new PeopleWindow { DataContext = people }.ShowDialog<bool>(this);

    private void OnListDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.EditCommand.CanExecute(null))
        {
            vm.EditCommand.Execute(null);
        }
    }
}
