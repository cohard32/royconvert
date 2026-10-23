# File Converter (file-converter.io) — REA Analizi

Tarih: 2026-10-23
Hedef: File Converter v2.2 (`FileConverter-2.2-x64-setup.msi`, SHA-256 `78bb9755…3554`)
Yöntem: MSI sisteme kurulmadan `msiexec /a` ile açıldı; .NET derlemeleri REA ile statik incelendi (kod çalıştırılmadı).

## 1. Mimari (gözlenen)

| Bileşen | Ne yapıyor | Kanıt |
|---|---|---|
| `FileConverter.exe` | WPF arayüz + dönüştürme motoru. .NET Framework 4.8, x64, 172 tip, 1071 metot | REA `ev_6f8c474a…`, `ev_c5a3231d…` |
| `FileConverterExtension.dll` | Explorer sağ tık menüsü (SharpShell ile COM shell extension). Sadece menüyü kurar, exe'yi başlatır | REA `ev_9575ebd1…`, `ev_a334f0ab…` |
| `ffmpeg.exe` (8.0.1 essentials, gyan.dev) | Ses + video + GIF dönüştürme | Dosya + `-version` |
| `Magick.NET` + `Magick.Native-Q16-x64.dll` | Resim dönüştürme (png/jpg/webp/avif/ico, döndürme, ölçekleme) | Dosya listesi |
| `gsdll64.dll` / `gswin64c.exe` (Ghostscript) | PDF okuma/sayfa sayfa resme çevirme | Dosya listesi |
| `Ripper.dll`, `yeti.mmedia.dll` | Ses CD'sinden (CDA) parça çıkarma | Dosya listesi + `ConversionJob_ExtractCDA` |
| Microsoft Office (kullanıcıda kurulu) | doc/xls/ppt → pdf için COM ile Word/Excel/PowerPoint açılıyor | `winword.exe`, `excel.exe`, `powerpnt.exe` + `App Paths` registry string'leri |
| `Settings.default.xml` | 76 hazır ayar (preset). Kullanıcı kopyası `%LOCALAPPDATA%\FileConverter\Settings.user.xml` | XML dosyası + string |

### Akış (sağ tık → dönüştürme)
1. Kullanıcı Explorer'da dosya(lar) seçip sağ tıklar → `File Converter` alt menüsü, uzantıya uyan presetler listelenir (`DisplayPresetIcons`, `Configure presets...`).
2. Eklenti, seçili dosya yollarını `%TEMP%\file-converter-input-list-{n}.txt` dosyasına yazar.
3. `FileConverter.exe --conversion-preset "<preset adı>" --input-files "<liste.txt>"` başlatılır (`--settings` ise ayar penceresini açar).
4. Exe, her dosya için bir `ConversionJob` oluşturur (`ConversionJobFactory`); işlemci sayısına göre paralel iş parçacığı sayısı belirlenir.
5. Küçük ilerleme penceresi (MainWindow) her iş için ilerleme + kalan süre gösterir; ffmpeg `-progress pipe:1` çıktısı okunur.
6. İş bitince çıktı dosya adı şablonla oluşur, isteğe bağlı olarak giriş dosyası silinir/arşivlenir (`InputPostConversionAction`).

### Dönüştürme işi türleri (`FileConverter.ConversionJobs`)
`ConversionJob_FFMPEG`, `ConversionJob_Gif`, `ConversionJob_ImageMagick`, `ConversionJob_Ico`, `ConversionJob_ExtractCDA`, `ConversionJob_Word`, `ConversionJob_Excel`, `ConversionJob_PowerPoint` (`ConversionJob_Office` taban sınıfı).

## 2. Desteklenen formatlar (Settings.default.xml)

**Girdi (58 uzantı):** 3gp 3gpp aac aiff ape arw avi avif bik bmp cda cr2 dds dng doc docx exr flac flv gif heic ico jfif jpeg jpg m4a m4b m4v mkv mov mp3 mp4 mpeg mpg nef odp ods odt oga ogg ogv opus pdf png ppt pptx psd raf rm svg tga tif tiff ts vob wav webm webp wma wmv xcf xls xlsx

**Çıktı (17):** Mp4, Mkv, Webm, Ogv, Avi, Gif, Mp3, Ogg, Flac, Wav, Aac, Png, Jpg, Webp, Avif, Ico, Pdf

**Preset grupları (76 adet):** `To X`, `Scale 75%/…`, `Scale 25%/…`, `Scale 720p/…`, `Scale 1080p/…`, `Rotate left/…`, `Extract DVD to …`, `Extract CDA to …`, `To Png (paged)` (PDF'i sayfa sayfa).

**Preset ayar anahtarları:** EnableAudio, VideoQuality, VideoEncodingSpeed, VideoScale, VideoRotation, VideoFramesPerSecond, AudioBitrate, AudioChannelCount, AudioEncodingMode, ImageQuality, ImageScale, ImageRotation, ImageMaximumSize, ImageClampSizePowerOf2, EnableFFMPEGCustomCommand, FFMPEGCustomCommand.

**Çıktı adı şablonu:** `(p)` = klasör, `(f)` = dosya adı, `(n:i) of (n:c)` = sayfa no / toplam. Örnek: `(p)(f) (720p)`, `(p)(f)-small`.

## 3. FFmpeg komut şablonları (ikiliden çıkarılan string'ler)

```
ortak:   -n -progress pipe:1 -i "<girdi>" ... "<çıktı>"
mp4:     -c:v libx264 -preset {hız} -crf {kalite}  + -c:a aac -q:a {..}
  NVIDIA: -hwaccel cuda -hwaccel_output_format cuda ; -preset {0} -rc constqp -qp {1}
  AMD:    -usage transcoding -quality {0} -qp_i {1} -qp_p {2} -qp_b {3}
webm:    -c:v libvpx-vp9 -crf {0} -b:v 0 (veya -lossless 1) + -c:a libvorbis -qscale:a {0}
ogv:     -codec:v libtheora -qscale:v {0} + -codec:a libvorbis -qscale:a {0}
avi:     -c:v mpeg4 -vtag xvid -qscale:v {0}  + -c:a libmp3lame -qscale:a {0}
mp3:     -codec:a libmp3lame -q:a {0}   (VBR)  |  -codec:a libmp3lame -b:a {0}k (CBR)
ogg:     -vn -codec:a libvorbis -qscale:a {0}
kanal:   -ac {0}
ölçek:   -vf scale=trunc(iw*{0}/2)*2:trunc(ih*{0}/2)*2   (CUDA: scale_cuda=…:format=yuv420p)
döndür:  transpose / "vflip,hflip" (180°)
gif:     fps={0} + palet üretimi (-lavfi)
```
Hız seçenekleri: UltraFast … VerySlow (x264 preset adları). Donanım hızlandırma: Off / CUDA / AMF.

## 4. Kendi uygulamamıza (Windows + macOS) uyarlama

| Özellik | Orijinal (sadece Windows) | Bizim çözüm |
|---|---|---|
| Sağ tık menüsü | SharpShell COM eklentisi | **Windows:** Win11 `IExplorerCommand` (sparse package) + klasik registry verb yedeği. **macOS:** Finder Sync Extension veya Quick Action (Automator/App Extension) |
| Video/ses | ffmpeg.exe | ffmpeg (Win: exe, macOS: universal binary). Donanım: Win NVENC/AMF/QSV, macOS **VideoToolbox** (`h264_videotoolbox`) |
| Resim | Magick.NET | ImageMagick CLI (`magick`) veya libvips; HEIC için macOS'ta `sips` de var |
| PDF → resim | Ghostscript | Ghostscript **veya** MuPDF/pdfium (lisans durumuna göre) |
| Office → PDF | MS Office COM (kurulu olmalı) | **LibreOffice headless** (`soffice --headless --convert-to pdf`), her iki platformda aynı |
| CD ripping | Ripper.dll (Win API) | İlk sürümde kapsam dışı önerilir |
| Ayarlar | XML | JSON (`%APPDATA%` / `~/Library/Application Support`) |
| Arayüz | WPF | Çapraz platform (aşağıda) |

## 5. Lisans uyarıları (önemli)
- File Converter **GPL-3.0**. Kodunu/preset dosyasını birebir kopyalarsak bizim proje de GPL-3.0 olmak zorunda. Kapalı kaynak/ticari düşünülüyorsa davranışı kendimiz yeniden yazmalıyız (bu analiz bunun için).
- Gömülü ffmpeg "essentials" build'i GPL (libx264 içerir).
- Ghostscript **AGPL** veya ücretli ticari lisans.
- ImageMagick (Apache-tarzı) ve LibreOffice (MPL, ayrı süreç olarak çağrılır) sorunsuz.

## 6. Açık sorular / bilinmeyenler
- Arayüz davranışı çalıştırılarak gözlenmedi (sadece statik analiz).
- GIF palet komutunun tam hali ve döndürme `transpose` değerleri birleştirilmiş string'lerden kısmen çıkarıldı; kesin hali için metot gövdeleri incelenebilir.
- Güncelleme servisi (`UpgradeService`) hangi URL'yi kullanıyor, incelenmedi.
