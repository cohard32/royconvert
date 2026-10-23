# Durum ve sonraki adımlar

Son güncelleme: 2026-10-23

## Tamamlananlar
- REA ile File Converter v2.2 analizi → `01-analiz-file-converter.md`
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
  - Windows klasik sağ tık menüsü (HKCU, `ExtendedSubCommandsKey` ile alt menüler)
- Testler: 83/83 (Core 71, gerçek ffmpeg ile uçtan uca dahil; App 12)

## Açık konular
1. **Resim menüsünde "Rotate left/right" görünmedi** (Windows 10, kullanıcının ekran görüntüsü). Kayıt defterinde 9 giriş var,
   Explorer'da 7'si görünüyor. Kırpılmış ekran görüntüsü mü yoksa Explorer sınırı mı, henüz doğrulanmadı.
   Video menüsünde 18 giriş var; orada da kesilme olup olmadığına bakılmalı.
2. **Windows 11 modern sağ tık menüsü zorunlu** (kullanıcının diğer PC'si Win 11). Klasik registry menüsü Win 11'de
   yalnızca "Diğer seçenekleri göster" altında çıkar. Gerekli: `IExplorerCommand` COM eklentisi + sparse MSIX paket
   kimliği (imzalı). Win 10'da klasik menü kalmalı → iki yol birlikte desteklenecek.
3. Menü şu an Debug derlemesini gösteriyor (`src\RoyConvert.App\bin\Debug\net8.0\royconvert.exe`).
   Kaldırmak için: `royconvert --unregister-shell`.
4. Pencere kapanırken gelen dosya çok dar bir aralıkta kaybolabilir.
5. Arayüz İngilizce; ikon Avalonia varsayılanı.

## Sıradaki adımlar (öneri sırası)
1. Açık konu 1'i doğrula/düzelt
2. Windows 11 modern menü (IExplorerCommand + sparse package), Win 10 klasik menü ile birlikte
3. Türkçe/İngilizce arayüz + ikon
4. Kurulum paketi (ffmpeg gömülü)
5. macOS (Finder Quick Action / Finder Sync) — Mac gerekiyor
