# 01 — Bağlantı ve kimlik (Faz 1)

## Hedefler

- Oyuncu hangi mağazadan alırsa alsın (Steam, Epic…) aynı sunucuya girebilsin.
- Mod uyumsuzluğu bağlanmadan anlaşılsın ve oyuncuya ne yapması gerektiği söylensin.
- Tek bir bozuk ya da kötü niyetli paket sunucuyu (yani barındıranın oyununu) çökertemesin.

## Akış

```
oyuncu                                   sunucu
  | -- bağlantı isteği + Hello ------------> |  protokol, oyun sürümü, mod sürümü,
  |                                          |  ban, izin listesi, modüller, doluluk
  | <------------- ret + RejectMessage ----- |  (uyumsuzsa; oturum hiç açılmaz)
  | <------------- Challenge (32 bayt) ----- |  (uyumluysa)
  | -- Auth (imza + şifre kanıtı) ---------> |  imza, şifre
  | <------------- Welcome + PlayerList ---- |
```

- **Hello bağlantı isteğinin içinde gelir.** Uyumsuz oyuncu için sunucuda hiç durum tutulmaz.
- **El sıkışma 10 saniyede bitmeli.** Bitmezse bağlantı kesilir (`AuthTimeout`).
- **Ret nedenleri sabit bir listedir** (`RejectReason`). Mod farkları üç liste olarak taşınır: eksik, fazla ve sürümü farklı.

## Kimlik

- İlk açılışta ECDSA P-256 anahtarı üretilir ve `Belgeler\Mount and Blade II Bannerlord\CoopKeep\identity.txt` dosyasına yazılır.
- **Oyuncu kimliği:** açık anahtarın SHA-256 özetinin ilk 16 baytı (32 onaltılık karakter).
- **Kanıt:** sunucunun rastgele sayısı `"CoopKeep-auth-v1:"` önekiyle imzalanır. Önek, imzanın başka bir yerde yeniden kullanılmasını engeller.
- **Şifre kanıtı:** `HMAC-SHA256(şifre, sayı ‖ kimlik)`. Şifre ağda açık gitmez.
- **Bozuk kimlik dosyası** silinmez, `.broken` adıyla saklanır; oyuncunun eski kimliği kurtarılabilir.
- **Doğrulama sonuçları:** .NET 8 testlerinde ve oyunun çalıştığı .NET Framework 4.x'te denendi. Geçersiz bir anahtar Windows'ta `PlatformNotSupportedException` fırlatıyor. Bu yakalanıyor, ayrıca bağlantı kabul edilmeden önce anahtar kontrol ediliyor.

## Sürüm eşleşmesi

- **Oyun sürümü:** yalnızca `vX.Y.Z` karşılaştırılır. Derleme numarası mağazalar arasında farklı olabileceği için dahil edilmez.
- **Modüller:** Id'ye göre (büyük/küçük harf duyarsız) ve sürüme göre karşılaştırılır. İçerik özeti iki tarafta da varsa o da karşılaştırılır; Faz 1'de özet boş.
- **Yalnızca oyuncuda olabilecek modlar:** sunucu sahibi `clientOnlyModules` ile belirtir. Örnek: oyun durumunu değiştirmeyen arayüz modları.

## Thread modeli

- Ağ olayları yalnızca `Poll()` çağrılınca işlenir. Oyunda `Poll()` her karede oyun thread'inden çağrılır.
- Faz 2'de oyun nesnelerine kilitsiz dokunmak bu sayede mümkün olacak.

## Açık konular (sonraki fazlar)

- Port açamayanlar için NAT delme ve aracı sunucu.
- İsteğe bağlı Steam lobisi ve daveti.
- Modül içerik özeti: aynı sürüm numarasıyla değiştirilmiş modları yakalamak için.
- LiteNetLib'i başka modların sürümüyle çakışmasın diye kendi derlememize gömmek.
