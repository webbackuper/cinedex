using System.IO.Compression;
using Cinedex.App.Services;
using Cinedex.Core.Titles;
using Cinedex.Data;

namespace Cinedex.Tests;

/// <summary>Catalogue consulté sur téléphone : import d'une vraie sauvegarde faite par l'application de bureau.</summary>
public sealed class PortableCatalogTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string DesktopDump(string title)
    {
        var desktop = Path.Combine(_temp.Path, "bureau-" + Guid.NewGuid().ToString("N"));
        var db = new CatalogDatabase(Path.Combine(desktop, "cinedex.db"));
        db.Initialize(desktop);
        var titles = new TitleRepository(db);
        var id = titles.Add(new NewTitle(TitleKind.Movie, title, 1995));
        var images = new ImageStore(db, Path.Combine(desktop, "images"));
        images.Attach(id, images.Save([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]), "poster");
        var settings = new SettingsStore(db);
        var service = new BackupService(new MaintenanceRepository(db), settings, "Mes films", desktop, Path.Combine(desktop, "backups"), images.ImagesDirectory);
        return service.CreateDump(new BackupSettings());
    }

    [Fact]
    public void A_desktop_backup_opens_on_the_phone_with_its_posters()
    {
        var phone = new PortableCatalog(Path.Combine(_temp.Path, "telephone"));
        Assert.False(phone.Exists);

        using (var dump = File.OpenRead(DesktopDump("Heat")))
        {
            Assert.Equal(1, phone.Import(dump));
        }

        var db = phone.Open();
        var titles = new TitleRepository(db);
        var heat = Assert.Single(titles.ListAll());
        Assert.Equal("Heat", heat.Title);
        var poster = new ImageStore(db, phone.ImagesDirectory).PathFor(heat.Id, "poster");
        Assert.True(File.Exists(poster), poster);

        // Un nouvel import remplace la copie précédente.
        using (var dump = File.OpenRead(DesktopDump("Ran")))
        {
            phone.Import(dump);
        }

        Assert.Equal(["Ran"], new TitleRepository(phone.Open()).ListAll().Select(t => t.Title));
        Assert.DoesNotContain(Directory.EnumerateDirectories(phone.Root), d => !d.EndsWith("catalog", StringComparison.Ordinal) && !d.EndsWith("backups", StringComparison.Ordinal));
    }

    [Fact]
    public void A_foreign_or_hostile_archive_is_refused_and_keeps_the_current_catalog()
    {
        var phone = new PortableCatalog(Path.Combine(_temp.Path, "telephone"));
        using (var dump = File.OpenRead(DesktopDump("Heat")))
        {
            phone.Import(dump);
        }

        var other = new MemoryStream();
        using (var zip = new ZipArchive(other, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("photos/vacances.jpg");
        }

        other.Position = 0;
        Assert.Throws<InvalidDataException>(() => phone.Import(other));
        Assert.Equal(["Heat"], new TitleRepository(phone.Open()).ListAll().Select(t => t.Title));

        // Chemin piégé dans l'archive : rien n'est écrit hors du dossier des images.
        var hostile = new MemoryStream();
        using (var zip = new ZipArchive(hostile, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var db = zip.CreateEntry("database/cinedex.db").Open())
            {
                using var current = new FileStream(phone.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                current.CopyTo(db);
            }

            using var evil = new StreamWriter(zip.CreateEntry("images/../../evil.txt").Open());
            evil.Write("x");
        }

        hostile.Position = 0;
        Assert.Equal(0, phone.Import(hostile));
        Assert.False(File.Exists(Path.Combine(phone.Root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(phone.CatalogDirectory, "evil.txt")));
    }
}
