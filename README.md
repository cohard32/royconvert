# royconvert

Windows ve macOS için sağ tık menüsünden dosya dönüştürme aracı.
Dosyaları seç → sağ tıkla → **royconvert** → hedef formatı seç.

## Durum
Geliştirme aşamasında (v0.1 hedefi).

## v0.1 kapsamı
- Video: mp4, mkv, webm, avi, gif (+ ölçekleme 25% / 75% / 720p / 1080p, döndürme)
- Ses: mp3, ogg, flac, wav, aac
- Resim: png, jpg, webp, avif, ico, pdf (tek sayfa; uzun kenar en çok A4); "PDF (tek dosya)": seçili resimler doğal
  sırayla tek PDF'te (`ilk-dosya (birleştirilmiş).pdf`)
- Belge: Office (doc, docx, xls, xlsx, ppt, pptx, odt, ods, odp, rtf) → PDF (Windows'ta kuruluysa Microsoft Word/Excel/PowerPoint,
  yoksa ya da hata verirse LibreOffice);
  PDF → png, jpg, webp (her sayfa ayrı dosya: `belge-01.png`, `belge-02.png`…; 150 dpi)
- Windows Explorer sağ tık menüsü
- macOS Finder entegrasyonu (Quick Action)

Sonraki sürümler: DVD çıkarma.

## Kullanım
```
royconvert                                   ana pencere (dosya seç / sürükle-bırak, sağ tık menüsünü kur/kaldır)
royconvert --preset 720p/mp4 a.mov b.mkv     dönüştür (ilerleme penceresi)
royconvert --preset mp3 --input-list l.txt   dosya listesini metin dosyasından oku (--temp-input-list: okuyup siler)
royconvert --list-presets                    tüm preset kimlikleri
royconvert --register-shell                  Windows sağ tık menüsünü kur (bu kullanıcı; Win 11 compact menü dahil)
royconvert --unregister-shell                menüyü kaldır
```
Sağ tık menüsü `royconvert_shell.dll` (C++ COM eklentisi) tarafından kurulur; seçili tüm dosyalar tek süreçle iletilir.
Açık bir pencere varsa yeni dosyalar ona (named pipe) eklenir.

## Mimari
| Klasör | Açıklama |
|---|---|
| `src/RoyConvert.Core` | Presetler, dönüştürme işleri, FFmpeg/ImageMagick komut üretimi (platformdan bağımsız) |
| `src/RoyConvert.App` | Avalonia arayüzü: ilerleme penceresi, ayarlar; Türkçe/İngilizce (`Resources/Strings*.resx`) |
| `src/RoyConvert.Shell` | Explorer sağ tık eklentisi (C++): Win 10 `IContextMenu`, Win 11 `IExplorerCommand` + sparse paket |
| `tests/RoyConvert.Core.Tests` | Motor testleri (gerçek ffmpeg ile uçtan uca dahil) |
| `tests/RoyConvert.App.Tests` | Komut satırı, tek-örnek, sağ tık menüsü ve çeviri testleri |
| `docs/` | Analiz ve tasarım notları |

## Geliştirme
Gereken: .NET 8 SDK; Windows'ta ayrıca Visual Studio (Build Tools) C++ x64 araçları ve Windows 10 SDK (sağ tık eklentisi için).

```
dotnet build
dotnet test
dotnet run --project src/RoyConvert.App
```

## Kurulum paketi (Windows)
Gereken: yukarıdakiler + [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```
`artifacts\royconvert-<sürüm>-setup.exe` üretir: .NET gerektirmeyen (self-contained) uygulama, sağ tık eklentisi,
ffmpeg ve LibreOffice (sürümleri ve SHA-256'ları betikte sabit; indirilip doğrulanır). LibreOffice MSI'ı kurulmadan
açılır (`msiexec /a`) ve `tools\LibreOffice`'e konur; sisteme LibreOffice kurulmaz. Kurulum varsayılan olarak Program Files'a yapılır;
`/CURRENTUSER` ile (ya da kurulumdaki seçimle) yönetici izni olmadan kullanıcı klasörüne kurulur. Kurulum sağ tık
menüsünü kurar, kaldırma onu da kaldırır.

ffmpeg GPLv3 lisanslıdır ve kurulumda ayrı bir program olarak bulunur; lisansı ve kaynak bilgisi `tools\` klasöründedir.
LibreOffice MPL-2.0 lisanslıdır; lisans dosyaları `tools\LibreOffice\` içindedir.

## Kullanılan araçlar
- [Avalonia UI](https://avaloniaui.net/)
- [FFmpeg](https://ffmpeg.org/) (ayrı süreç olarak çalıştırılır)
- [Magick.NET](https://github.com/dlemstra/Magick.NET)
- [LibreOffice](https://www.libreoffice.org/) (Office → PDF; ayrı süreç olarak, `--headless`)
- [Docnet.Core](https://github.com/GowenGit/docnet) / PDFium (PDF sayfalarını çizme)
