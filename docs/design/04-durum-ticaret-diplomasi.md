# 04 — Oyuncunun durumu, yerleşim, diplomasi

Oyuncunun dünyası sunucunun bir kopyasıdır ve kendi başına ilerlemez ([03](03-senkron-ve-koruma.md)). Her değişiklik sunucuda olur; oyuncu ekranında doğrusunu sunucunun gönderdikleriyle görür. Bu belge, konumların ötesinde neyin nasıl taşındığını anlatır.

## Kendi klanı

- Karakter oluşturulurken oyuncuya kendi klanı kurulur: ad (oyuncu yazar, hazır öneri gelir), kültür, sancak, ilk yerleşim.
- Oyunun bu sürümünde hazır bir "boş klan kur" işlevi yok: yalnızca yoldaşı lorda çeviren ve isyancı klan kuran işlevler var, ikisi de başka işler yapıyor. Klan oyunun açık adımlarıyla kurulur; sonunda oyunun "klan kuruldu" olayı tetiklenir ve sancağı, haydutlarla savaşı o olayı dinleyen davranışlar tamamlar. Haydutlarla savaş yine de ayrıca denetlenir.
- Varsayılan parti şablonunun dışarıdan yazılabilen bir ayarı yok; alanı yansıtmayla ayarlanır. Bulunamazsa klan şablonsuz kalır, oyun bozulmaz.
- Oyuncunun kendi dünyasında "oyuncunun klanı" da onun klanı yapılır, böylece klan ekranı ve sancak onu gösterir. Bu ayar dışarı açık değil; yansıtmayla yazılır, olmazsa barındıranın klanı görünmeye devam eder.

## Oyuncunun durumu (`HeroState`)

- Sunucu her yarım saniyede bir her oyuncunun durumunu hazırlar: para, askerler (yaralı ve deneyimle), esirler, eşyalar, savaş ve sivil takım, klanın adı, sancağı, kademesi, şöhreti, nüfuzu ve krallığı. Parmak izi değiştiyse gönderir.
- Oyuncu bunu kendi kopyasına uygular. Kahraman askerler (yoldaşlar, esir lordlar) o dünyada olmayabilir; onlara dokunulmaz.
- Bir ekran açıkken (envanter, parti) durum uygulanmaz, oyuncunun yaptığının üstüne yazılırdı. Ekran kapanınca önce değişiklik gönderilir, sonra sunucunun yeni durumu gelir.

## Envanter ve parti ekranı

Ekranlar oyunun kendi ekranlarıdır ve oyuncunun dünyasında açılır. Kapanınca son hâl sunucuya gider:

- **Envanter (`InventoryCommit`):** parti eşyaları ve takım. Sunucu eski ve yeni sayımı karşılaştırır. Ticaret dışında eşya yaratılamaz; yer değiştirir ya da atılır. Her takım yuvasına uygun eşya mı diye bakılır.
- **Ticaret:** aynı mesaj, yerleşimin kimliğiyle. Fark alınmış ya da satılmış eşyadır. Fiyatı sunucu kendi modeliyle hesaplar; oyuncunun parası ya da tüccarların parası yetmezse ticaret bütünüyle reddedilir.
- **Parti (`PartyCommit`):** yalnızca terhis, yükseltme, esir salma ve esirden asker kabul edilir. Yükseltme sağlıklı askerle, yeterli deneyimle, oyunun altın bedeliyle ve gerekiyorsa eşyayla (atlı birlik için at) yapılır. Esirden asker, oyunun "boyun eğmiş esir" sayısı kadar olur.
- Kabul edilmeyen değişiklikte oyuncuya neden yazılır ve doğru durum geri gelir.
- Satış eylemi parametre sırası belgelenmediği için kullanılmadı: eşya ve altın doğrudan taşınıyor. Bunun bedeli, oyuncu ticaretinin pazar fiyatlarını henüz etkilememesi.

## Yerleşim

- Parti sunucuda bir yerleşime girince sunucu pazarı ve gönüllüleri gönderir (`SettlementInfo`). Oyuncunun dünyasında da parti oraya konur, böylece oyunun yerleşim bilgisi ve ticaret ekranı doğru çalışır.
- Yerleşim penceresi kendiliğinden açılır; F7 yeniden açar. İçinde ticaret (oyunun ekranı, sunucunun stoku ve tüccarların parası), gönüllü toplama ve ayrılma var.
- Gönüllüler oyunun kuralıyla açılır: ileri gelenle ilişki arttıkça daha çok yuva. Kapalı yuvalar gri görünür.

## Diplomasi (F8)

- **Kim karar verir:** savaşa ve barışa yalnızca bağımsız bir klanın lideri ya da bir hükümdar karar verir. Vasal karar veremez; bu oyunun kuralı.
- **Savaş:** doğrudan ilan edilir; savaş oyuncuların taraflarına (klan ya da krallık) açılır.
- **Anlaşma:** teklif edenin verdiği ve istediği. Para, eşya ve kale ya da şehir olabilir; barış şartı eklenebilir. Hiçbir şey istenmiyorsa hediyedir: onay beklenmez, değeri kadar ilişki artar (1000 altın değerine 1, en çok 10).
- **Evlilik:** kendi klanından biri ile başka bir oyuncunun klanından biri; kadın ve erkek, ikisi de yetişkin ve evli değil, akraba değil. Teklif o klanın liderine gider. İki oyuncu evlenirse ikisi de kendi klanının lideri kalır; oyunun evlilik eylemi birini diğerinin klanına geçirirdi. Diğer evliliklerde evlenen, lider olanın klanına geçer. Klanlar savaştaysa evlilik barış getirir. Çocuklar oyunun kendi kuralıyla doğar (eşler aynı yerdeyken).
- **Krallık:** vasal olarak katılma (klan kademesi 2), paralı asker olma (kademe 1), ayrılma, kendi krallığını kurma (kademe 3 ve bir kale ya da şehir). Hükümdar bir oyuncuysa katılma isteğine o karar verir.
- **Teklifler:** karşı tarafın onayını isteyen her şey bir tekliftir ve iki dakikada düşer. Kabulde iki taraf yeniden denetlenir; biri tutmazsa hiçbir şey uygulanmaz.
- Sunucu penceresi değilse barındıran da oyuncudur: aynı pencereyi kullanır, ona gelen teklifler kendi ekranında açılır.

## Kaynak koruması ve oyuncunun işlemleri

Korunan oyuncunun azalan kaynakları, zaman akarken olmuşsa geri konur ([03](03-senkron-ve-koruma.md)). Sunucu penceresinde zaman başka oyuncular için aktığından, yerleşimdeki oyuncunun alışverişi aynı karede "zamanla azalma" sanılabilirdi; bu da bedava alışveriş demekti. Her oyuncu işleminden (ticaret, toplama, parti, anlaşma) hemen sonra koruma yeni değerleri kabul eder.

## Dünyadaki değişiklikler

- Sunucu savaşları, barışları, kale ve şehir el değiştirmelerini, klanların krallık değiştirmesini, evlilikleri ve oyuncu ailelerindeki doğumları herkese duyurur (`WorldEvent`).
- Oyuncu, iki taraf da kendi dünyasında varsa değişikliği uygular; böylece haritanın renkleri güncel kalır. Haber olarak yalnızca oyuncuları ilgilendirenler gösterilir.
- **Sonradan çıkan partiler:** konum paketinde oyuncunun dünyasında olmayan bir parti görülürse oyuncu onu sorar (`PartyInfoRequest`, saniyede bir, en çok yüz). Sunucu adını, sancağını, asker sayısını ve türünü söyler (`PartySpawn`). Oyuncu bir benzerini koyar; sunucu "yok oldu" deyince kaldırır. Klanı o dünyada yoksa yalnızca adı ve sancağıyla bir klan kurulur. Bu dünya kaydedilmediği için iz bırakmaz.

## Bilinen sınırlar

- Oyuncunun ekranındaki fiyatlar eski kopyadan gelir, biraz farklı olabilir. Geçerli olan sunucunun fiyatıdır ve sonuç oyuncuya yazılır.
- Oyuncu ticareti pazar fiyatlarını henüz etkilemiyor.
- Savaşlar hâlâ sunucuda otomatik çözülür; birlikte savaş sonraki adım.
