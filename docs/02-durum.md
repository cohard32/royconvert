# Durum ve sonraki adımlar

Son güncelleme: 2026-10-25

## Tamamlananlar
- REA ile File Converter v2.2 analizi → `01-analiz-file-converter.md`; sağ tık eklentisi analizi → `03-analiz-fc-kabuk-eklentisi.md`
- Proje iskeleti: Avalonia 11.3.9 + .NET 8 (Avalonia 12, .NET 9 derleyicisi istediği için 11'de kalındı)
- `RoyConvert.Core` dönüştürme motoru
  - 76 preset (`PresetCatalog`): düz format, MP4 (small), Scale 75/50/25%, 720p, 1080p (yalnızca küçültür), Rotate left/right
  - FFmpeg komut üretimi + `-progress pipe:1` ilerleme okuma
  - Magick.NET resim dönüştürme
  - `ConversionRunner`: sonradan iş eklenebilen paralel kuyruk, çıktı adı çakışmasını önler, var olan dosyanın üzerine yazmaz
- `RoyConvert.App`
  - İlerleme penceresi (iptal, "Göster", başarıyla bitince otomatik kapanma)
  - Ana pencere (dosya seç / sürükle-bırak, menüyü kur/kaldır)
  - Tek-örnek: Explorer'ın çoklu seçimde açtığı süreçler dosyalarını named pipe ile ilk pencereye iletir
- `RoyConvert.Shell` (C++ DLL, `royconvert_shell.dll`) — sağ tık menüsü (2026-10-25)
  - Eski kayıt defteri menüsü Explorer'da **16 öğede kesiliyordu** (Rotate kaybolma sorununun nedeni; video menüsünde 720p/1080p/Rotate de yoktu)
  - Win 10 klasik menü: `IShellExtInit` + `IContextMenu`, HKCU `SystemFileAssociations\.ext\shellex\ContextMenuHandlers\royconvert`
  - Win 11 compact menü: `IExplorerCommand` (`{F350E4E0-…}`) + imzasız sparse paket `royconvert_shell.msix`
    (`src\RoyConvert.Shell\package\AppxManifest.xml`, yayıncıda imzasız-paket OID'i). Yalnızca Win 11'de (build ≥ 22000)
    `Add-AppxPackage -AllowUnsigned -ExternalLocation <exe klasörü>` ile kurulur (`WindowsModernMenu`); başarısız olursa UAC ile
    yükseltilmiş olarak yeniden dener. Klasik menü Win 11'de "Diğer seçenekleri göster" altında da kalır
  - Menü verisi: `royconvert --register-shell` exe yanına `shell-menu.txt` yazar. Çoklu seçimde yalnızca tüm uzantıları kabul eden presetler gösterilir
  - Tıklanınca tek `royconvert.exe --preset <id> "<dosyalar>"` süreci; uzun seçimde `--input-list` (%TEMP%\rcv*.tmp)
  - `dotnet build` DLL'i Visual Studio C++ araçlarıyla derleyip exe yanına kopyalar (`src\RoyConvert.Shell\build.cmd`)
  - Doğrulama (menüyü Explorer'ın yoluyla `IContextMenu` üzerinden döküp çalıştırarak): jpg 29/29, mp4 45/45 öğe; Rotate, 720p,
    3 dosya (Türkçe ad dahil) ve 400 dosya (liste dosyası) dönüştürüldü; jpg+mp4 karışık seçimde menü gizli.
    Kullanıcı Win 10 Explorer'da gerçek sağ tıkla doğruladı (2026-10-25)
  - Win 11 yolu bu Win 10 PC'de sınanabildiği kadar (2026-10-25):
    - `IExplorerCommand` doğrudan sürüldü (Explorer'ın sırası: GetState→GetTitle→EnumSubCommands→Invoke): jpg 24, mp4 45 yaprak,
      karışık seçimde `ECS_HIDDEN`, çoklu seçimde Invoke dönüştürdü
    - makeappx şema doğrulaması geçiyor (`/nv` yalnızca "exe paketin içinde" denetimini atlar; bozuk manifest yine hata verir)
    - Geliştirici modunda manifest (OID'siz kopya) `-Register -ExternalLocation` ile kaydedildi: Status Ok, COM sınıfı kayıt
      defteri olmadan paketten etkinleşti, Win 10 menüsünde çift giriş oluşmadı; sonra kaldırıldı
    - PowerShell betiklerinin çıkış kodları: kaldırma (paket yok) 0, kurulum hatası 1
- Dil ve ikon (2026-10-25)
  - Arayüz İngilizce + Türkçe: `src\RoyConvert.App\Resources\Strings.resx` / `Strings.tr.resx` (derlemede `Strings` sınıfı üretilir,
    XAML'da `{x:Static res:Strings.…}`). Varsayılan Windows dili; ana penceredeki "Dil" listesinden değişir, anında uygulanır
    (pencere yeniden kurulur) ve `%APPDATA%\royconvert\settings.json`'a kaydedilir
  - Preset adları da çevrilir (`PresetNames`, id parçasına göre: `scale75` → "%75 küçült", `rotate-left` → "Sola döndür",
    `mp4-small` → "MP4 (küçük)"); sağ tık menüsü etiketleri de seçili dilde (`shell-menu.txt` dil değişince yeniden yazılır).
    Çıktı dosya adları (ör. "(rotated)") değişmedi
  - İkon: `Assets\icon\royconvert.svg` → `Assets\royconvert.ico` (16–256 px) ve Win 11 paket logoları; exe ikonu
    (`ApplicationIcon`), pencereler ve sağ tık menüsü kök öğesi bunu kullanır
  - Doğrulama: Türkçe/İngilizce ana pencere ve Türkçe ilerleme penceresi ekran görüntüsüyle; UI Automation ile dil listesinden
    English → System default seçildi (pencere, ayar dosyası, menü etiketleri değişti); menü dökümünde Türkçe etiketler ve ikon var
- Testler: 95/95 (Core 71; App 24). Dil kültürüne dokunan testler ortak, paralel çalışmayan koleksiyonda (`UiCultureCollection`)

## Açık konular
1. **Win 11'de gerçek deneme yapılmadı** (kullanıcının Win 11 PC'si). Sınanamayanlar: `-AllowUnsigned` kurulumu (Win 10'da bu
   parametre yok ve OID'li yayıncıyı "imzasız ad alanında değil" diye reddediyor), yönetici izni gerekip gerekmediği,
   compact menüde görünüm. Denemek için: `royconvert --register-shell` → sağ tık. Sorun olursa çıkış kodu 1 ve hata mesajı verir.
   Kalıcı dağıtımda Microsoft imzalı paket öneriyor (imzasız paket "test için"): proje herkese açılırken kod imzalama sertifikası düşünülmeli.
2. Explorer menüsü hemen değişmezse: `taskkill /f /im explorer.exe & start explorer`.
3. DLL Explorer tarafından yüklüyken yeniden derlenirse kopyalama uyarı verir; eski DLL kalır (Explorer'ı yeniden başlatınca düzelir).
4. Menü şu an Debug derlemesini gösteriyor (`src\RoyConvert.App\bin\Debug\net8.0\`). Kaldırmak için: `royconvert --unregister-shell`.
5. `--input-list` geçici dosyaları %TEMP%'te kalıyor.
6. Pencere kapanırken gelen dosya çok dar bir aralıkta kaybolabilir.
7. **Sparse paket kaydı klasör izinlerini değiştiriyor:** Win 10'daki geliştirici modu testinde `-ExternalLocation` olarak verilen
   `bin\Debug\net8.0`'ın izinleri korumalı hale geldi, paket kaldırılınca geri gelmedi ve derleme yeni alt klasöre (`tr\`)
   yazamadı. `icacls … /grant "<kullanıcı>:(OI)(CI)F" /T` ile düzeltildi. Win 11'de Debug klasöründen `--register-shell` aynı
   sorunu çıkarabilir; kurulum paketi Program Files'a kurulacağı için orada sorun olmaması beklenir (denenmedi).
8. Çıktı dosya adı ekleri ("(rotated)", "-small" vb.) İngilizce kalıyor; istenirse çevrilebilir.

## Sıradaki adımlar (öneri sırası)
1. Win 11 PC'de `royconvert --register-shell` ile compact menüyü dene (kod hazır)
2. Kurulum paketi (ffmpeg gömülü; DLL, paket ve kayıt dahil)
3. macOS (Finder Quick Action / Finder Sync) — Mac gerekiyor
