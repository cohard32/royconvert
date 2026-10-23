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

## Mimari
| Klasör | Açıklama |
|---|---|
| `src/RoyConvert.Core` | Presetler, dönüştürme işleri, FFmpeg/ImageMagick komut üretimi (platformdan bağımsız) |
| `src/RoyConvert.App` | Avalonia arayüzü: ilerleme penceresi, ayarlar |
| `tests/RoyConvert.Core.Tests` | Birim testleri |
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
