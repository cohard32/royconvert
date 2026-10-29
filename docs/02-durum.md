# Durum ve sonraki adımlar

Son güncelleme: 2026-10-27

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
- Kurulum paketi (2026-10-25): `installer\build-installer.ps1` + `installer\royconvert.iss` (Inno Setup 6, Türkçe/İngilizce)
  - Self-contained win-x64 yayın + `royconvert_shell.dll/.msix` + ffmpeg 9.0.2 essentials (gyan.dev, SHA-256 sabit) `tools\`
    altında; kurulum dosyası ~70 MB. Uygulama sürümü csproj `<Version>` (0.1.0)
  - Program Files (yönetici) veya `/CURRENTUSER` (yöneticisiz). Sonda `--register-shell` kurulumu başlatan kullanıcı olarak
    çalışır; kaldırırken `--unregister-shell`. Explorer'ın kilitlediği DLL yeniden başlatmada değiştirilir/silinir
  - Menü dosyası artık kullanıcıya özel: `%LOCALAPPDATA%\royconvert\shell-menu.txt` (Program Files'a yazılamaz; her kullanıcının
    dili ayrı)
  - Doğrulama: core testleri paketteki ffmpeg 9.0.2 ile 71/71; `/CURRENTUSER /VERYSILENT` kurulum → menü kurulu DLL'e bağlı,
    döküm ve çalıştırma tamam, uygulama `tools\ffmpeg.exe`'yi kullanıyor → sessiz kaldırma: klasör, kayıt defteri ve menü dosyası
    temizlendi. Program Files (yönetici) kurulumu ve Win 11 denenmedi
- Belge dönüştürme (2026-10-26, `belge-donusturme` dalı)
  - **Office → PDF**: `Office\OfficeConverter` — `soffice --headless --convert-to pdf`; her iş kendi geçici profilini
    (`-env:UserInstallation`) ve çıktı klasörünü kullanır (aynı profille iki soffice çalışamaz), sonra `%TEMP%\royconvert-office-*`
    silinir. Girişler: doc, docx, xls, xlsx, ppt, pptx, odt, ods, odp, rtf (preset `pdf`). soffice bazen hata vermek yerine
    takılıyor (ör. rtf → pptx) → 10 dk zaman aşımı, süreç ağacı öldürülür
  - `ToolLocator.FindLibreOffice`: `ROYCONVERT_SOFFICE` → `tools\LibreOffice\program\soffice.exe` (pakettekini) →
    Program Files / `/Applications` → PATH. Bulunamazsa ana pencerede uyarı, işte "LibreOffice was not found…" hatası
  - **PDF → resim**: `ImageConverter` PDF girişini Docnet.Core (PDFium, MIT; win/linux/osx-x64/arm64 yerel dosyalarıyla) ile
    150 dpi (`PdfDpi`) çizer, beyaz zemine yapıştırır, sonra Magick.NET ile yazar. png/jpg/webp presetleri pdf'i de kabul eder.
    Tek sayfa → `belge.png`; çok sayfa → `belge-01.png`, `belge-02.png`… (`ImageConverter.PagePaths`, ≥2 hane, var olanın
    üzerine yazmaz). Hata/iptalde yazılan sayfalar silinir. "Göster" çıktı yoksa klasörü açar
  - Kurulum paketi LibreOffice 26.8.1.1'i gömer: betik MSI'ı (downloadarchive, SHA-256 sabit) indirir, `msiexec /a` ile
    kurmadan açar; VC++ DLL'leri (`System64`) `program\`'a, yazı tipleri (`Fonts`) `share\fonts\truetype`'a taşınır;
    `share\extensions` (58 eklenti, çoğu sözlük) silinir — **yeni profilde bu eklentileri kaydederken soffice takılıyordu**
    (eklentisiz 9 sn'de dönüştürdü). Açılmış hali ~1,6 GB
  - Doğrulama: sistemdeki LibreOffice (winget ile bu PC'ye kuruldu, 26.8.1.1) ve açılmış paket kopyasıyla Office testleri;
    uygulamanın komut satırından docx/xlsx/pptx → PDF ve bu PDF'ler → PNG (2 sayfalı rapor `rapor-01/02.png`), Türkçe
    karakterler doğru. `--register-shell` sonrası `.pdf`/`.docx` vb. menü kayıtları var
- **Resim → PDF** (2026-10-26): `pdf` preseti hem Office hem resim girişlerini kabul eder; `PdfConverter` girişe göre
  Magick.NET'e (resim) ya da LibreOffice'e (belge) yönlendirir, LibreOffice yoksa yalnız belgeler başarısız olur. Her resim
  ayrı bir tek sayfalı PDF olur. Sayfa boyutu = piksel / dpi; dpi yoksa 72 varsayılır ve uzun kenar A4'ü (297 mm) aşarsa dpi
  yükseltilir (piksel yeniden örneklenmez). JPEG girişler PDF içinde JPEG kalır (21 KB jpg → 24 KB pdf), diğerleri Zip
- `--temp-input-list` (2026-10-26): sağ tık eklentisi uzun seçimlerde listeyi bununla verir; uygulama okuyup siler
  (eski açık konu 5). `--input-list` dosyası silinmez. Explorer üzerinden uzun seçimle uçtan uca denenmedi (DLL derlendi)
- **Resimleri tek PDF'te birleştirme** (2026-10-26): `combine-pdf` preseti ("PDF (tek dosya)", `Preset.CombinesInputs`).
  `ProgressViewModel.Add` seçili tüm dosyalardan tek `ConversionJob(inputs, preset)` yapar, dosyalar doğal sırada
  (`NaturalComparer`: sayfa2 < sayfa10). `IConverter.CombineAsync` (varsayılanı NotSupported) → `ImageConverter` her resmi
  tek PDF'e sayfa yapar (sayfa boyutu/JPEG kuralları tek resimdeki gibi; tüm resimler bellekte açılır). Çıktı ilk dosyanın
  adıyla: `sayfa1 (birleştirilmiş).pdf`. İlerleme penceresinde "sayfa1.png ve 3 dosya daha". Menü tek seçimde de gösteriyor
  (sağ tık eklentisi yalnızca uzantıya bakıyor). Explorer daha sonra açılan ikinci bir seçimi ayrı iş olarak ekler
- **Çıktı adı ekleri arayüz dilinde** (eski açık konu 8): `PresetNames.WithLocalizedFileName`, iş oluşturulurken
  `FileName_<grup id>` kaynağıyla: "-küçük", " (döndürülmüş)", " (birleştirilmiş)"; %/720p ekleri aynı kalır
- **Microsoft Office ile PDF** (2026-10-27, `ms-office-pdf` dalı): kullanıcı günlük işte Word kullanıyor.
  `Office\MicrosoftOfficeConverter` kurulu Word/Excel/PowerPoint'i COM (geç bağlama, `dynamic`) ile STA iş parçacığında
  sürer. Dosya salt okunur açılır, makrolar kapalı (`AutomationSecurity = 3`), kaydetmeden kapatılır; son kullanılanlara
  eklenmez. Şifreli dosyaya sahte şifre verilir → istem çıkmadan hemen hata (PowerPoint: `dosya::şifre::`). Aynı anda tek
  belge (`Gate`). Yalnız kendi başlattığı Office sürecini kapatır (öncesi/sonrası süreç listesi); PowerPoint kullanıcının
  açık örneği olabilir, ona dokunmaz. Zaman aşımında kendi sürecini öldürür
  - `IDocumentConverter.Priority`: doc/docx/rtf/xls/xlsx/ppt/pptx → önce Microsoft Office (0), sonra LibreOffice (1);
    odt/ods/odp → önce LibreOffice. `PdfConverter` biri hata verirse sıradakini dener, hepsi hata verirse hepsinin mesajı
  - Ana pencere "Microsoft Office: Word, Excel, PowerPoint" ve LibreOffice satırlarını gösterir; ikisi de yoksa uyarı
  - Bu PC'de PowerShell'in `New-Object -ComObject Word.Application`'ı tür kitaplığı hatası veriyor (`0x80029C4A`, CurVer
    `.12` kalıntısı); uygulamanın IDispatch yolu etkilenmiyor
  - Doğrulama: docx/xlsx/pptx (Türkçe adlarla) → PDF, kaynak SHA-256 ve yazma zamanı aynı, `~$` dosyası yok, Office süreci
    kapanıyor; üçünde de şifreli dosya ~10 sn'de hata. Uygulamadan rtf → PDF 9 sn, PDF üreticisi "Microsoft Word 2019"
  - Ön sürüm: https://github.com/cohard32/royconvert/releases/tag/v0.1.0-test2 (9e15473'ten, 312 MB,
    SHA-256 `daa5b5fe28fdf479d14a88521b54c3dafc50f68e71263dd6d299df9d89e0b752`). Bu setup kurulup denenmedi
- **PDF birleştirme ve bölme** (2026-10-27): `PdfPages` (Docnet/PDFium; sayfalar resme çevrilmeden kopyalanır, yazı
  seçilebilir kalır)
  - "PDF (tek dosya)" (`combine-pdf`) artık PDF de kabul eder: resim + PDF karışık seçim doğal sırada tek PDF. Yalnız
    resimse eski Magick yolu; değilse her resim geçici tek sayfalı PDF olur, sonra hepsi birleştirilir
  - PDFium sayfaları ilk belgeye aktarıyor ve bazı geçerli dosyaları hedef olarak reddediyor ("failed to merge files");
    ilk belge önce PDFium'un kendi yazdığı kopyaya çevriliyor (`Split(0..n-1)`), sonra birleştiriliyor
  - "PDF (sayfalara böl)" (`split-pdf`, `Preset.SplitsPages`): `belge-sayfa-01.pdf`, `-02`… (EN `-page`). Tek sayfalı
    PDF'te hata ("nothing to split"); hata/iptalde yazılanlar silinir
  - Doğrulama: uygulamadan Word PDF'i + PNG + PDF → birleştir → böl: Word sayfalarında gömülü yazı tipi, resim sayfasında
    resim var
- **Ayarlar penceresi** (2026-10-27): ana penceredeki "Ayarlar…" → `SettingsWindow` / `SettingsViewModel`. Resim kalitesi
  (10–100, kaydırıcı), video kalitesi (Yüksek CRF 18 / Normal 23 / Küçük dosya 28), PDF → resim dpi (96/150/200/300), aynı
  anda dönüşüm sayısı (Otomatik = çekirdek/2 ya da 1…çekirdek). `AppSettings`'e (`settings.json`) yazılır; varsayılan değerler
  null saklanır. Elle yazılmış aralık dışı değerler yüklenirken sınırlanır, listede olmayan değer seçenek olarak eklenir
  - `AppSettings.ApplyTo`: `ProgressViewModel.Add` her işe uygular; preset'in kendi değeri varsayılandan farklıysa (ör.
    `mp4-small` CRF 28) dokunmaz. Sağ tık süreçleri ayarları açılışta okur
  - Doğrulama: ekran görüntüsü (UI Automation ile düğmeye basıldı); `PdfDpi: 300` ile uygulamadan PDF → PNG genişliği
    1275 → 2550
- **DVD → MP4/MKV** (2026-10-27, gerçek diskle denenmedi; kullanıcının şu an DVD'si yok): `Dvd\DvdDisc` sürücü kökü,
  VIDEO_TS'i içeren klasör ya da VIDEO_TS'in kendisini açar. Ana başlık = `VTS_nn_1..9.VOB` toplamı en büyük başlık seti
  (`VTS_nn_0` menü, `VIDEO_TS.VOB` atlanır). ffmpeg `concat:` ile parçaları tek akış okur; `-analyzeduration/-probesize 100M`,
  `-fflags +genpts`, `-map 0:v:0 -map 0:a?` (tüm ses dilleri, altyazı yok), `bwdif=mode=send_frame:deint=interlaced`
  - Presetler `dvd/mp4`, `dvd/mkv` (`Preset.ReadsDvd`; `Accepts` klasörde DVD arar). Ana penceredeki "DVD…" (klasör
    seçici), klasör/sürücü sürükle-bırak, komut satırı `--preset dvd/mp4 <klasör>` ve sağ tık menüsü
  - **Sağ tık (2026-10-28, `dvd-menu` dalı):** menü dosyasında DVD presetlerinin uzantısı yerine `<dvd>`; eklenti
    `Directory` ve `Drive` için de kayıtlı (HKCU). Seçilen her öğe DVD klasörü/sürücüsüyse (`VIDEO_TS` ya da onu içeren
    klasör/kök, içinde `VTS_nn_1..9.VOB`) "royconvert › DVD › MP4/MKV" görünür; başka klasörde, karışık seçimde menü
    yok. Boş disket/kart okuyucuda "disk takın" çıkmasın diye `SEM_FAILCRITICALERRORS`. Sürücü kökü `"E:\"` komut
    satırında `E:"` okunuyordu → `Quote` sondaki ters eğik çizgileri ikiler. Win 11 sparse paketine `Directory` eklendi
    (Win 11'de denenmedi; sürücü kökü Win 11'de yalnız "Diğer seçenekler"de)
  - Canlı doğrulama (Win 10, Debug menü kaydı, Explorer'ın `IContextMenu`'su ile): sahte DVD klasörü ve `VIDEO_TS` →
    DVD › MP4/MKV; boş klasör, DVD + boş klasör, `C:\` → menü yok; VOB dosyası → normal video menüsü. Menüden
    DVD › MP4 → yanına `Tatil DVD.mp4` (h264 + aac, 6 sn); `subst R:` ile sürücü kökü → DVD › MKV → Videolar\DVD.mkv
  - Çıktı: optik sürücüdeyse Videolar klasörüne `<birim etiketi>.mp4`; diske kopyalanmış klasörse yanına `<klasör adı>.mp4`
  - CSS korumalı ticari DVD'ler çözülmez (ffmpeg hata verir ya da bozuk görüntü). Ev/kamera kayıtları hedef
  - Doğrulama: sahte disk (ffmpeg `-target pal-dvd`, taramalı, 16:9, iki ses dili, iki parçaya sektör sınırında bölünmüş +
    menü + kısa ikinci başlık): 6 sn tam süre, 2 ses, progressive, 25 fps, DAR 16:9; bozuk VOB → hata, çıktı yok.
    Uygulamadan `--preset dvd/mp4` ile 8 sn'lik sahte disk dönüştü
- **Resimden bilgileri silme** (2026-10-28, `resim-pdf` dalı): "Bilgileri sil (EXIF, konum) › PNG/JPG/WEBP/AVIF"
  (`strip-metadata/*`, `ConversionSettings.StripMetadata`). Önce `AutoOrient` (resim dik kalır), sonra `Strip()`; ICC renk
  profili geri konur. JPEG → JPEG kaynağın kalitesiyle yazılır (kalite kaybı en az). Çıktı `<ad> (bilgisiz).jpg`
- **PDF döndürme** (aynı dal): "Sola/Sağa döndür › PDF" (`rotate-left/pdf`, `rotate-right/pdf`). Yalnız sayfaların
  `/Rotate` değeri değişir (metin ve içerik aynen kalır), mevcut dönüşün üstüne eklenir. Önce PDFium ile açılabilirlik
  denetlenir (bozuk/şifreli → anlaşılır hata, çıktı yok)
  - Kütüphane: **PDFsharp 6.2.4 (MIT)**. 6.5.0 ve sonrası **AGPL-3.0** → csproj'da sabitlendi, lisans kararı olmadan
    yükseltilmez
  - Canlı doğrulama (Debug menü kaydı, Explorer `IContextMenu`): GPS + yön 6 + yorum + sRGB'li 400×200 JPEG → menüden
    Bilgileri sil › JPG → 200×400, EXIF/GPS/yorum yok, ICC var, kalite 80; LibreOffice'in yazdığı 2 sayfalık PDF → Sağa
    döndür › PDF → sayfalar 90°, PNG'ye çevrilince 1753×1240 (yatay). Sonra menü Program Files test4'e geri bağlandı
- Testler: 170/170 (Core 124; App 46).

## Gerçek DVD denemesi (DVD bulununca)
1. Diski tak; Bu bilgisayar'da DVD sürücüsüne sağ tık → royconvert → DVD → MP4 (ya da royconvert'i aç → "DVD…" →
   DVD sürücüsünü seç → "DVD › MP4" → Dönüştür)
2. Videolar klasöründe `<disk adı>.mp4` oluştu mu; süre filmin tamamı mı (menü/fragman değil), ses ve görüntü eş mi
3. Birden fazla ses dili varsa oynatıcıda seçilebiliyor mu; 4:3 / 16:9 görüntü basık ya da uzamış mı
4. Hata olursa ilerleme penceresindeki mesajı kopyala (CSS korumalı disk olabilir) Dil kültürüne dokunan testler ortak, paralel çalışmayan koleksiyonda (`UiCultureCollection`)

## Açık konular
1. **Win 11'de gerçek deneme yapılmadı** (kullanıcının Win 11 PC'si). Sınanamayanlar: `-AllowUnsigned` kurulumu (Win 10'da bu
   parametre yok ve OID'li yayıncıyı "imzasız ad alanında değil" diye reddediyor), yönetici izni gerekip gerekmediği,
   compact menüde görünüm. Denemek için: `royconvert --register-shell` → sağ tık. Sorun olursa çıkış kodu 1 ve hata mesajı verir.
   Kalıcı dağıtımda Microsoft imzalı paket öneriyor (imzasız paket "test için"): proje herkese açılırken kod imzalama sertifikası düşünülmeli.
2. Explorer menüsü hemen değişmezse: `taskkill /f /im explorer.exe & start explorer`.
3. DLL Explorer tarafından yüklüyken yeniden derlenirse kopyalama uyarı verir; eski DLL kalır (Explorer'ı yeniden başlatınca düzelir).
4. Menü şu an Debug derlemesini gösteriyor (`src\RoyConvert.App\bin\Debug\net8.0\`). Kaldırmak için: `royconvert --unregister-shell`.
5. ~~`--input-list` geçici dosyaları %TEMP%'te kalıyor~~ → `--temp-input-list` ile çözüldü (2026-10-26).
6. ~~Pencere kapanırken gelen dosya çok dar bir aralıkta kaybolabilir~~ → çözüldü (2026-10-27): `SingleInstance` her
   mesaja onay/ret baytıyla cevap verir (uzunluk önekli JSON; gönderen onay almadan çıkmaz). Pencere kapanma kararını
   `TryStopAccepting` ile kabulü durduran kilidin içinde verir; o anda yolda olan (`ProgressViewModel.Receive` ile UI'a
   gönderilmiş ama eklenmemiş) dosya varsa kapanmaz. Kapanırken gelen dosya reddedilir, gönderen 10 sn boyunca yeniden
   dener ve mutex boşalınca kendi penceresini açar. Doğrulama: 6 test (testler ayrı boru adı kullanır, açık royconvert
   bozmaz) ve 3 tur × 20 süreç (0–2,6 sn rastgele aralıkla, 2 sn kapanma beklemesine denk gelecek şekilde) → 60/60 dönüştü
7. **Sparse paket kaydı klasör izinlerini değiştiriyor:** Win 10'daki geliştirici modu testinde `-ExternalLocation` olarak verilen
   `bin\Debug\net8.0`'ın izinleri korumalı hale geldi, paket kaldırılınca geri gelmedi ve derleme yeni alt klasöre (`tr\`)
   yazamadı. `icacls … /grant "<kullanıcı>:(OI)(CI)F" /T` ile düzeltildi. Win 11'de Debug klasöründen `--register-shell` aynı
   sorunu çıkarabilir; kurulum paketi Program Files'a kurulacağı için orada sorun olmaması beklenir (denenmedi).
8. ~~Çıktı dosya adı ekleri İngilizce kalıyor~~ → arayüz diline çevriliyor (2026-10-26).
9. Program Files kurulumu yönetici olmayan bir hesapta başka bir yöneticinin şifresiyle yapılırsa, kaldırma menü kaydını kurulumu
   yapan hesap yerine yönetici hesabından silmeye çalışır (Inno'da kaldırma adımı "asıl kullanıcı olarak" çalıştırılamıyor).
10. Kurulum dosyası imzasız: Windows SmartScreen "tanınmayan uygulama" uyarısı verir (kod imzalama sertifikasıyla çözülür).
11. LibreOffice'li kurulum paketi (2026-10-26): `build-installer.ps1`'i kullanıcı çalıştırdı (Claude'a `-ExecutionPolicy Bypass`
    izni otomatik denetimde reddediliyor; Git Bash'te `!` ile yol `E:/royconvert/...` yazılmalı). `royconvert-0.1.0-setup.exe`
    **312 MB**. `/CURRENTUSER /VERYSILENT` kurulum → kurulu uygulama docx/pptx → PDF'i paketteki
    `tools\LibreOffice\program\soffice.bin` ile yaptı → sessiz kaldırma klasörü sildi; menü yeniden Debug'a bağlandı.
    Sınanmayanlar: VC++ kurulu olmayan makinede `program\` yanındaki DLL'lerin yetmesi, gömülü yazı tiplerinin
    `share\fonts\truetype`'tan yüklenmesi (bu PC'de ikisi de sistemde var), Program Files kurulumu. GitHub ön sürümü güncellenmedi
    (v0.1.0-test1 hâlâ LibreOffice'siz)
12. Office dönüşümü yeni profille ~4,5 sn (ilk/soğuk çalıştırmada ~9 sn). **Profil yeniden kullanılamıyor:** aynı profille ya da
    ilk çalıştırmadan kalan profilin kopyasıyla soffice (26.8.1.1, `--nolockcheck --norestore` dahil) ~1 sn CPU sonra
    sonsuza dek bekliyor (2026-10-26 ölçüldü). Bu yüzden her iş yeni profil kullanıyor; hızlandırma denemesi bırakıldı
13. macOS'ta LibreOffice gömülü değil; `/Applications/LibreOffice.app` aranıyor

## Kaldığımız yer (2026-10-27)
- Tüm işler `main`'de: `kurulum-paketi` PR #1 (2026-10-25, kullanıcı birleştirdi), `belge-donusturme` + `ms-office-pdf`
  PR #2 (2026-10-27, 2bcb7d8). PR'ı Claude açar, birleştirmeyi kullanıcı yapar (otomatik denetim "incelemesiz birleştirme"yi
  reddediyor)
- Ön sürüm `v0.1.0-test3` (main 2bcb7d8'den; Office/Word → PDF, PDF birleştir/böl, ayarlar, DVD) GitHub'a yüklendi,
  SHA-256 `b65085199a499616c8610c8ecc89d39df5d2f4c1c70da5a5689744dd52d620da`. Bu PC'de `/CURRENTUSER /VERYSILENT` kurulum:
  menü kurulu DLL'e bağlandı, menü dosyasında yeni seçenekler var; kurulu uygulamayla rtf → PDF (Word 2019, 9,6 sn),
  PDF bölme, resim + PDF birleştirme, sahte DVD → MP4 çalıştı, arkada Office süreci kalmadı. Sessiz kaldırma klasörü,
  menü dosyasını ve kayıtları sildi; menü yeniden Debug'a bağlandı. Program Files (yönetici) kurulumu denenmedi
- Bu oturumda yapılanlar sırasıyla: Microsoft Office ile PDF, PDF birleştirme/bölme, ayarlar penceresi, DVD (sahte diskle)
- Win 11 / Mac denemeleri: kullanıcı kendisi söyleyince yapılacak, hatırlatılmayacak. DVD denemesi disk bulununca

## Kaldığımız yer (2026-10-29)
- PR #8 (`pdfium-cokme`, PDFium doğrudan) ve PR #9 (`pdf-kucult` = `pdf-sikistir`'in main'e rebase'i, "PDF (küçült)")
  birleşti (main 0c9a0c6); 177 test. Eski uzak `pdf-sikistir` dalı duruyor (zorla push otomatik denetimde reddedildi)
- Ön sürüm **`v0.1.0-test5`** (main 0c9a0c6), https://github.com/cohard32/royconvert/releases/tag/v0.1.0-test5,
  SHA-256 `74a0776ea70d110aa76699f6f16f3a9bdd73953af519fe24931782a36b25847a` (derleme tekrarlanabilir: yeniden derleme
  aynı SHA'yı verdi). Program Files'a test4 üzerine kuruldu; **bu PC'de menü Program Files test5'e bağlı**
- Canlı deneme sağ tık menüsünden (`E:\rc-deneme\menudump`, IContextMenu dökümü + `--invoke`; **STA iş parçacığı şart**,
  yoksa royconvert menüsü görünmez): EXIF silme (GPS gitti), 3 fotoğraf → tek PDF, PDF küçült 15,5 MB → 987 KB,
  sağa döndür, PDF → PNG; 4 PDF aynı anda PNG + `DOTNET_GCgen0size=0x20000` 15 turda 15/15, olay günlüğünde çökme yok
- `build-installer.ps1` bu PC'de `-ExecutionPolicy Bypass` olmadan çalışıyor (CurrentUser Unrestricted) → Claude derler.
  **Yükleme sürerken `artifacts`'ta yeniden derleme setup'ı bozar** (Inno üzerine yazdı, 950 KB kaldı) → yüklemeyi
  ayrı bir kopyadan yap
- **Toplu yeniden adlandırma** (`toplu-ad` dalı): menünün sonunda "Yeniden adlandır…" (menü dosyasında `rename` satırı,
  desteklenen tüm uzantılar; DLL değişmedi, `--preset rename` → `CommandKind.Rename`, tek örnek kullanılmaz). Pencere:
  kalıp (`{n}` sıra, `{name}` eski ad), başlangıç numarası, anlık önizleme, sorunlu satır kırmızı, düğme kapalı.
  `Core/BatchRename.cs`: doğal sıra, numara son numaranın genişliğine göre sıfırla dolar, önce geçici adlar (yer
  değiştirme / yalnız büyük-küçük harf çalışır), hata olursa hepsi geri alınır. Canlı (Debug, menüden): jpg+pdf karışık
  seçim, aynı ad uyarısı, adlandırma, kilitli dosyada geri alma + Türkçe mesaj, kilit açılınca tekrar deneme tamam
- UI otomasyonu: `E:\rc-deneme\ui.ps1` (UIAutomation ile yaz/tıkla, PrintWindow ile ekran görüntüsü)
- PR #11 (toplu ad) birleşti (main 46d69d6)
- **Filigran** (`filigran` dalı): menüde "Filigran ekle…" (`MenuTools`: pencere açan menü öğeleri, `rename` ile
  birlikte menü dosyasının sonunda). Pencere: yazı, konum (ortada çapraz / sağ alt / döşe), görünürlük, boyut, ilk
  dosyanın anlık önizlemesi. `Core/Watermark.cs`: resim Magick.NET (Arial Bold dosyası; Magick'in varsayılan yazı
  tipinde ğ yok), PDF PDFsharp ile vektör yazı (metin olarak aranabilir). Yeni dosya "(filigranlı)" ekiyle, asıl dosya
  değişmez; HEIC/DNG/PSD/TGA → JPG; GIF/ICO/SVG/EXR yok. Tuzaklar: PDFsharp saydamlığı PDF < 1.4'te yok sayıyor
  (sürüm 1.4'e çekiliyor); `/Rotate`'li sayfada `XGraphics` döndürülmemiş koordinatta çiziyor (dönüşüm ekleniyor);
  döşemede ızgara yazı doğrultusunda kurulmalı (yatay ızgarada üst üste biniyordu)
- macOS'ta PDFsharp için yazı tipi çözücü gerekecek (`UseWindowsFontsUnderWindows` yalnız Windows)
- PR #12 (filigran) birleşti (main aa25281). Ön sürüm **`v0.1.0-test6`** (main aa25281),
  https://github.com/cohard32/royconvert/releases/tag/v0.1.0-test6, SHA-256
  `8ce1b542a54ee5a836822e068baa4ae821bdb74b9763171dcca1b3704b10d016`. Program Files'a test5 üzerine kuruldu; menüden
  döşeli filigran (2 JPG + PDF), yeniden adlandırma, PDF küçült, PDF → PNG tamam. **Bu PC'de menü Program Files test6'ya bağlı**
- **Ses/video** (`ses-video` dalı): videodan ses çıkarma zaten vardı (MP3/AAC/FLAC…). Eklenenler:
  - "Sesi kaldır" (`mute/*` ön ayarları, `EnableAudio = false`): kaynakla aynı tür ya da MKV ise video kopyalanır
    (`FfmpegArguments.CanCopyVideo`, hızlı ve kayıpsız), değilse kodlanır
  - "Kırp…" (`MenuTools.Trim`, video + ses): başlangıç/bitiş (`MediaTrim.ParseTime`: 75, 1:15, 0:01:15.5, virgül
    de olur), bitiş boşsa sonuna kadar, aynı bölüm her dosyadan. Varsayılan akış kopyası (`-ss` + `-c copy`; video
    önceki ana kareden başlayabilir); "tam karesinden kes" videoyu normal dönüşüm ayarlarıyla yeniden kodlar
    (royconvert'in yazmadığı türler, ör. MOV → MP4). Ses hep kopyalanır. Süre `ffmpeg -i` çıktısından okunuyor
    (`FfmpegConverter.ProbeDurationAsync`); ilerleme kesilen bölüme göre
  - Canlı (Debug, menüden): sesi kaldır MP4 (h264 kopya, ses yok), mp4+mp3 0–3 sn kopya kesim, hatalı aralık
    uyarısı, MOV 0:05,5–0:12 tam kare → 6,5 sn MP4
- Sıradaki: kullanıcıya sor (test7 ön sürümü / yeni özellik)

## Kaldığımız yer (2026-10-28 gece, kullanıcı "bugünlük yeter, kaydet" dedi)
- **`pdfium-cokme` dalı (push edildi, PR açılmadı) — önce bu birleşmeli:** Docnet ile PDF işlemleri (PDF → resim,
  birleştir, böl) ara sıra **tüm programı çökertiyordu** (AccessViolation, `FPDFDOC_ExitFormFillEnvironment`,
  `DocReader.Dispose`). Neden: Docnet'in `DocumentWrapper._formInfo` (`FPDF_FORMFILLINFO`) yönetilen sınıf olarak
  marshal ediliyor; PDFium geçici kopyanın adresini saklıyor. GC/bellek yeniden kullanımı arttıkça çökme artıyor
  (tek dosyada sık GC ile 12'de 2; 4 PDF birlikte daha sık; sıkıştırma testleriyle test süreci her seferinde çöktü).
  Bizim kilitle çözülmedi (denendi). Çözüm: `Core/Pdfium.cs` `PdfiumDocument` — pdfium.dll'i doğrudan P/Invoke ile
  çağırır, form ortamı yok (FPDF_ANNOT), belge baytı kapanana kadar yerel bellekte, tüm çağrılar tek kilitte.
  Docnet artık yalnız pdfium.dll'i getirmek için paket olarak duruyor; kodda/testlerde kullanılmıyor
  - Doğrulama: `dotnet test` 3 kez 170/170 (+2 yeni: paralel+GC, bozuk PDF). Uygulama stres (4 PDF aynı anda PNG,
    `DOTNET_GCgen0size=0x20000`): eski test4 15'te 1 çökme + 6 eksik çıktı; yeni 15'te 0/0, ardından 40'ta 0/0.
    İzole Docnet test programı çökmeyi üretemedi (yalnız uygulamada görülüyor)
- **`pdf-sikistir` dalı (1d04f81, push edildi, PR yok):** "PDF (küçült)" (`compress-pdf`, `Core/PdfCompression.cs`):
  içteki RGB/gri 8 bit resimler sayfaya göre 150 dpi'ye küçültülüp JPEG 75; yalnız küçülürse değişir (kayıpsız
  resim yarıya inmezse dokunulmaz), SMask/CMYK/indexed atlanır; sonuç büyükse hata, çıktı yok. Canlı: 5,7 MB →
  231 KB, LibreOffice PDF'i 644 → 139 KB, 150 dpi'de gözle fark yok. **pdfium-cokme birleşince bu dal main'e rebase
  edilip (PdfPages.EnsureReadable → yeni `Open`) testler çalıştırılmalı, sonra PR**
- PDFsharp ad çözümü: dolaylı nesneler `PdfReference.Value`, dolaylı isimler `PdfNameObject` (PdfName değil)
- Ortam: başka bir Claude Code oturumu açılırken scratchpad'i ve `%TEMP%` altındaki klasörleri siliyor → denemeler
  `E:\rc-deneme`'de yapıldı (silindi)
- `main`'de ek olarak: PR #4 (pencere kapanırken dosya kaybı), PR #5 (DVD sağ tık menüsü, b959553), PR #7 (EXIF silme,
  PDF döndürme)
- **PDF → Word** `pdf-word` dalında bekliyor (PR yok): bu PC'nin Word'ü elle de PDF açamıyor, Office güncellemesi 17002
  ile kurulamıyor; ayrıntı o daldaki bu dosyada
- Ön sürüm **`v0.1.0-test4`** (main b959553), https://github.com/cohard32/royconvert/releases/tag/v0.1.0-test4,
  SHA-256 `1619f472d6d16080066e7a903c8b8ace517c62887a28a8c81565e3eaf23ddbcf` (312 MB, yükleme ~25 dk)
  - `/CURRENTUSER /VERYSILENT`: kurulum, sahte DVD klasörü menüsü → MP4, kaldırma (klasör/menü dosyası/Directory-Drive
    kayıtları silindi) tamam
  - **Program Files (yönetici, `/ALLUSERS`, UAC) ilk kez denendi:** kullanıcının 27 Ekim'deki test3 kurulumunun üzerine
    yükseltme → dosyalar ve sağ tık DLL'i yerinde güncellendi, yeniden başlatma gerekmedi; menü kurulumu başlatan
    kullanıcının HKCU'suna Program Files DLL'iyle kaydoldu. Menüden DVD → MP4, PNG → JPG ve **gömülü LibreOffice ile
    ODT → PDF** (Producer "LibreOffice 26.8.1.1", Türkçe metin doğru) çalıştı. Yönetici kaldırma: klasör, menü dosyası,
    tüm HKCU menü kayıtları ve Başlat kısayolu silindi. Ardından yeniden kuruldu
  - **Bu PC'de menü şu an Program Files'taki test4'e bağlı** (Debug'a değil). Geliştirmede Debug'a bağlamak için
    `src\RoyConvert.App\bin\Debug\net8.0\royconvert.exe --register-shell`
- Sıradaki öneri: aşağıdaki listeden kullanıcıya seçenek sun

## Win 11 denemesi (kullanıcı söyleyince)
- En güncel test kurulumu: https://github.com/cohard32/royconvert/releases/tag/v0.1.0-test4
- Win 11 denemesinde talimatlar **tek tek** verilecek (kullanıcı tercihi). Önerilen sıra:
  1. Ön sürüm sayfasından setup'ı indir (SmartScreen: "Ek bilgi" → "Yine de çalıştır")
  2. Varsayılan (Program Files, yönetici) kurulum, "Sağ tık menüsünü kur" işaretli
  3. Bir resme sağ tık → compact menüde "royconvert" var mı, alt menüler (Rotate, %75 küçült…) tam mı
  4. Bir öğeyi çalıştır → çıktı dosyası oluştu mu
  5. "Diğer seçenekleri göster" altındaki klasik menüde de var mı
  6. Hata olursa: `"C:\Program Files\royconvert\royconvert.exe" --register-shell` komutunun mesajı;
     `Get-AppxPackage royconvert.ContextMenu` çıktısı
  7. Kaldır → menü ve paket gitti mi (`Get-AppxPackage royconvert.ContextMenu` boş olmalı)

## Sıradaki adımlar (öneri sırası)
1. ~~DVD'ler için sağ tık menüsü (klasör/sürücü)~~ → `dvd-menu` dalı (2026-10-28); gerçek diskle deneme yine bekliyor
2. Kullanıcı söyleyince: Win 11 PC denemesi (yukarıdaki sıra), macOS (Finder Quick Action / Finder Sync)
3. Proje herkese açılmadan önce: kod imzalama sertifikası (SmartScreen ve Win 11 imzasız paket uyarıları)
