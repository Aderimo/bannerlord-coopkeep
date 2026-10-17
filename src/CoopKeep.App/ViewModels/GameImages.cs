using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CoopKeep.Core.Images;

namespace CoopKeep.App.ViewModels;

/// <summary>
/// Arayüzde gösterilen bir oyun görseli. Görsel arka planda indirilir; gelince
/// <see cref="Bitmap"/> dolar ve bağlı kontroller kendiliğinden güncellenir.
/// </summary>
public sealed partial class GameImage : ObservableObject
{
    [ObservableProperty] private Bitmap? _bitmap;

    public bool HasImage => Bitmap is not null;

    partial void OnBitmapChanged(Bitmap? value) => OnPropertyChanged(nameof(HasImage));
}

/// <summary>
/// <see cref="GameImageService"/>'in arayüz tarafı: aynı görsel bir kez çözülür ve
/// bütün sayfalarda paylaşılır.
/// </summary>
public static class GameImages
{
    private static readonly ConcurrentDictionary<string, GameImage> Cache = new();

    /// <summary>Testlerde ve ekran görüntülerinde ağa çıkılmasın diye kapatılabilir.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Görselli paket exe'nin yanındaki <c>images</c> klasöründe gelir; önce oraya bakılır.
    /// </summary>
    public static GameImageService Service { get; } = new(GameImageService.DefaultCacheDirectory, packDirectories:
    [
        System.IO.Path.Combine(AppContext.BaseDirectory, "images"),
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? "", "images"),
    ]);

    /// <summary>
    /// Görseli getirir. <paramref name="id"/> verilirse önce oyundan çıkarılmış görsele
    /// (iç kimlikle adlandırılmış) bakılır; yoksa ada göre wiki görseli aranır.
    /// </summary>
    public static GameImage Get(GameImageKind kind, string? name, string? id = null)
    {
        var key = kind + ":" + (id ?? "") + ":" + (name ?? "");
        return Cache.GetOrAdd(key, __ =>
        {
            var image = new GameImage();
            if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(id)) _ = LoadAsync(image, kind, name ?? "", id);
            return image;
        });
    }

    private static async Task LoadAsync(GameImage target, GameImageKind kind, string name, string? id)
    {
        try
        {
            var path = Service.TryGetById(kind, id)
                       ?? (string.IsNullOrWhiteSpace(name) ? null : Service.TryGetCached(kind, name))
                       ?? (Enabled && !string.IsNullOrWhiteSpace(name) ? await Service.GetAsync(kind, name).ConfigureAwait(false) : null);
            if (path is null) return;

            var bitmap = Decode(path);
            if (bitmap is not null) Dispatcher.UIThread.Post(() => target.Bitmap = bitmap);
        }
        catch (Exception)
        {
            // Görsel yoksa boş kalır; arayüz adı göstermeye devam eder.
        }
    }

    /// <summary>Dosyayı bir kez okuyup çözer (dosya kilitli kalmaz).</summary>
    public static Bitmap? Decode(string path)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
