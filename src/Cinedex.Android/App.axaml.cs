using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cinedex.Android.ViewModels;
using Cinedex.Android.Views;
using Cinedex.Core.Localization;
using Cinedex.Data;

namespace Cinedex.Android;

public sealed class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Langue du téléphone, si Cinedex la connaît (sinon français).
        Tr.Use(Tr.SystemDefault());
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cinedex");
            single.MainView = new MainView { DataContext = new CatalogViewModel(new PortableCatalog(root)) };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
