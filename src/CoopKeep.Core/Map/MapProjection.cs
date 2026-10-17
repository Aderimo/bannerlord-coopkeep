namespace CoopKeep.Core.Map;

/// <summary>
/// Oyun koordinatlarını harita görüntüsünün piksellerine çevirir.
/// </summary>
/// <remarks>
/// <para>
/// Harita görüntüsü, topluluk wiki'sindeki oyun içi Calradia haritası
/// (<c>Calradia_Bannerlord_Lite.png</c>, 1920×1272). CoopKeep görüntüyü taşımaz;
/// kullanıcının bilgisayarına indirip önbelleğe alır.
/// </para>
/// <para>
/// Dönüşüm 2026-10-16'da görüntüdeki 52 kasaba sancağının konumuyla
/// <c>settlements.xml</c>'deki konumların en küçük kareler uydurmasından çıkarıldı;
/// ortalama hata ~5 piksel. Oyunun y ekseni kuzeye büyüdüğü için <see cref="ScaleY"/>
/// eksidir.
/// </para>
/// </remarks>
public static class MapProjection
{
    public const int ImageWidth = 1920;
    public const int ImageHeight = 1272;

    public const double ScaleX = 2.3995;
    public const double OffsetX = -188.61;
    public const double ScaleY = -2.4232;
    public const double OffsetY = 1469.11;

    public static (double X, double Y) ToImage(double gameX, double gameY) =>
        (ScaleX * gameX + OffsetX, ScaleY * gameY + OffsetY);

    public static (double X, double Y) ToGame(double imageX, double imageY) =>
        ((imageX - OffsetX) / ScaleX, (imageY - OffsetY) / ScaleY);
}
