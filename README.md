# royconvert

Windows ve macOS için sağ tık menüsünden dosya dönüştürme aracı.
Dosyaları seç → sağ tıkla → **royconvert** → hedef formatı seç.

## Durum
Geliştirme aşamasında (v0.1 hedefi).

## v0.1 kapsamı
- Video: mp4, mkv, webm, avi, gif (+ ölçekleme 25% / 75% / 720p / 1080p, döndürme)
- Ses: mp3, ogg, flac, wav, aac
- Resim: png, jpg, webp, avif, ico
- Windows Explorer sağ tık menüsü
- macOS Finder entegrasyonu (Quick Action)

Sonraki sürümler: Office → PDF (LibreOffice), PDF → resim, DVD çıkarma.

## Kullanım
```
royconvert                                   ana pencere (dosya seç / sürükle-bırak, sağ tık menüsünü kur/kaldır)
royconvert --preset 720p/mp4 a.mov b.mkv     dönüştür (ilerleme penceresi)
royconvert --preset mp3 --input-list l.txt   dosya listesini metin dosyasından oku
royconvert --list-presets                    tüm preset kimlikleri
royconvert --register-shell                  Windows sağ tık menüsünü kur (yalnızca bu kullanıcı)
royconvert --unregister-shell                menüyü kaldır
```
Explorer çoklu seçimde her dosya için ayrı süreç başlatır; ilk süreç pencereyi açar, diğerleri dosyalarını
ona (named pipe) iletip kapanır, böylece tüm dosyalar tek pencerede görünür.

## Mimari
| Klasör | Açıklama |
|---|---|
| `src/RoyConvert.Core` | Presetler, dönüştürme işleri, FFmpeg/ImageMagick komut üretimi (platformdan bağımsız) |
| `src/RoyConvert.App` | Avalonia arayüzü: ilerleme penceresi, ayarlar |
| `tests/RoyConvert.Core.Tests` | Motor testleri (gerçek ffmpeg ile uçtan uca dahil) |
| `tests/RoyConvert.App.Tests` | Komut satırı ve tek-örnek testleri |
| `docs/` | Analiz ve tasarım notları |

## Geliştirme
Gereken: .NET 8 SDK.

```
dotnet build
dotnet test
dotnet run --project src/RoyConvert.App
```

## Kullanılan araçlar
- [Avalonia UI](https://avaloniaui.net/)
- [FFmpeg](https://ffmpeg.org/) (ayrı süreç olarak çalıştırılır)
- [Magick.NET](https://github.com/dlemstra/Magick.NET)
