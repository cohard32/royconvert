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
  - Presetler `dvd/mp4`, `dvd/mkv` (`Preset.ReadsDvd`; `Accepts` klasörde DVD arar). Sağ tık menü dosyasına girmez (yalnız
    dosyalar); ana penceredeki "DVD…" (klasör seçici) ya da klasör/sürücü sürükle-bırak, komut satırı `--preset dvd/mp4 <klasör>`
  - Çıktı: optik sürücüdeyse Videolar klasörüne `<birim etiketi>.mp4`; diske kopyalanmış klasörse yanına `<klasör adı>.mp4`
  - CSS korumalı ticari DVD'ler çözülmez (ffmpeg hata verir ya da bozuk görüntü). Ev/kamera kayıtları hedef
  - Doğrulama: sahte disk (ffmpeg `-target pal-dvd`, taramalı, 16:9, iki ses dili, iki parçaya sektör sınırında bölünmüş +
    menü + kısa ikinci başlık): 6 sn tam süre, 2 ses, progressive, 25 fps, DAR 16:9; bozuk VOB → hata, çıktı yok.
    Uygulamadan `--preset dvd/mp4` ile 8 sn'lik sahte disk dönüştü
- Testler: 165/165 (Core 120; App 45).

## Gerçek DVD denemesi (DVD bulununca)
1. Diski tak; royconvert'i aç → "DVD…" → DVD sürücüsünü seç → "DVD › MP4" → Dönüştür
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
- Ardından: açık konu 6 (pencere kapanırken dosya kaybı) PR #4 ile çözüldü (main 392a6b9)
- **Yarım kalan: PDF → Word (DOCX), `pdf-word` dalı (main'e birleştirilmedi, PR açılmadı).** `docx` preseti (yalnız PDF
  girişi), `MicrosoftOfficeConverter` Word'de PDF'i açıp `SaveAs2(wdFormatXMLDocument)`; `IDocumentConverter.Priority`
  artık çıktı biçimini de alır; `AvailablePresets` Word yoksa preseti ana pencereden ve sağ tık menüsünden çıkarır.
  Aynı dalda hata düzeltmesi: Office açarken hata verip belgeyi açık bırakırsa (`CloseInput`) Word görünür kalmaz
  - **Bu PC'de çalışmadı:** Word otomasyonla PDF açınca "Word PDF Files dönüştürücüsünü başlatamıyor" (gizli ve görünür
    Word'de aynı). `PDFREFLOW.EXE` var, CLSID `{A5C79653-FC73-46ee-AD3E-B64C01268DAA}` → Office16\PDFREFLOW.EXE doğru;
    WOW6432Node'da eski Office15 kaydı var; Word'de bekleyen Office güncellemesi var; olay günlüğünde iz yok
  - **Sıradaki adım (kullanıcı sonra yapacak):** Word'de elle Dosya → Aç → bir PDF. Elle de açamıyorsa sorun Office
    kurulumunda (güncelleme/onarım denenebilir); elle açıyorsa sorun yalnız otomasyonda. Çalışırsa
    `PdfToWord_MakesAnEditableDocument_AndLeavesThePdfUnchanged` testi geçmeli → PR aç. Çalışmazsa özellik bırakılabilir
    ama `CloseInput` düzeltmesi ayrıca main'e alınmalı
  - Bu dalda testler: 170'ten 169 geçiyor (yalnız yukarıdaki PDF → Word testi bu PC'de başarısız)
- Sıradaki öneri: önce PDF → Word denemesi; sonra aşağıdaki listeden kullanıcıya seçenek sun

## Win 11 denemesi (kullanıcı söyleyince)
- En güncel test kurulumu: https://github.com/cohard32/royconvert/releases/tag/v0.1.0-test3
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
1. DVD'ler için sağ tık menüsü (klasör/sürücü) — gerçek diskle denedikten sonra
2. Kullanıcı söyleyince: Win 11 PC denemesi (yukarıdaki sıra), macOS (Finder Quick Action / Finder Sync)
3. Proje herkese açılmadan önce: kod imzalama sertifikası (SmartScreen ve Win 11 imzasız paket uyarıları)
