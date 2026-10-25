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
royconvert --register-shell                  Windows sağ tık menüsünü kur (bu kullanıcı; Win 11 compact menü dahil)
royconvert --unregister-shell                menüyü kaldır
```
Sağ tık menüsü `royconvert_shell.dll` (C++ COM eklentisi) tarafından kurulur; seçili tüm dosyalar tek süreçle iletilir.
Açık bir pencere varsa yeni dosyalar ona (named pipe) eklenir.

## Mimari
| Klasör | Açıklama |
|---|---|
| `src/RoyConvert.Core` | Presetler, dönüştürme işleri, FFmpeg/ImageMagick komut üretimi (platformdan bağımsız) |
| `src/RoyConvert.App` | Avalonia arayüzü: ilerleme penceresi, ayarlar |
| `src/RoyConvert.Shell` | Explorer sağ tık eklentisi (C++): Win 10 `IContextMenu`, Win 11 `IExplorerCommand` + sparse paket |
| `tests/RoyConvert.Core.Tests` | Motor testleri (gerçek ffmpeg ile uçtan uca dahil) |
| `tests/RoyConvert.App.Tests` | Komut satırı ve tek-örnek testleri |
| `docs/` | Analiz ve tasarım notları |

## Geliştirme
Gereken: .NET 8 SDK; Windows'ta ayrıca Visual Studio (Build Tools) C++ x64 araçları ve Windows 10 SDK (sağ tık eklentisi için).

```
dotnet build
dotnet test
dotnet run --project src/RoyConvert.App
```

## Kullanılan araçlar
- [Avalonia UI](https://avaloniaui.net/)
- [FFmpeg](https://ffmpeg.org/) (ayrı süreç olarak çalıştırılır)
- [Magick.NET](https://github.com/dlemstra/Magick.NET)
