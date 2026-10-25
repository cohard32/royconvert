# royconvert — Claude notları

Sağ tık menüsünden dosya dönüştüren Windows (+ ileride macOS) uygulaması; file-converter.io benzeri.
Kullanıcı Türkçe konuşuyor. Denemelerde talimatları **tek tek** ver: bir adım yaz, sonucunu bekle, sonra sıradakini yaz.

**Devam etmeden önce `docs/02-durum.md` oku** — "Kaldığımız yer", açık konular ve sıradaki adımlar orada.

## Yığın
- Avalonia 11.3.9 + .NET 8 (`src/RoyConvert.App`), dönüştürme motoru `src/RoyConvert.Core` (FFmpeg, Magick.NET)
- Sağ tık eklentisi C++ (`src/RoyConvert.Shell`): Win 10 `IContextMenu`, Win 11 `IExplorerCommand` + imzasız sparse paket.
  `dotnet build` bunu Visual Studio C++ araçlarıyla derler (VS Build Tools C++ x64 + Windows 10 SDK gerekli)
- Kurulum: `installer/build-installer.ps1` (Inno Setup 6), ffmpeg sürümü ve SHA-256'sı betikte sabit
- Testler: `dotnet test` (App testlerinde dil kültürüne dokunanlar `UiCultureCollection`'da)

## Kurallar
- Menü hem Win 10 hem Win 11'de çalışmalı (kullanıcının iki bilgisayarı var).
- File Converter GPL-3.0: kodu ve preset XML'i birebir kopyalanmaz; yalnızca davranış incelenir (`docs/01-…`, `docs/03-…`).
- Dosya boyutu önemli değil.

## Ortam tuzakları
- Kaynak dosyalar CRLF; Bash heredoc/sed/JSON içinde ters eğik çizgiler bozulabiliyor → düzenlemelerde Edit/Write aracını kullan.
- Git Bash'te Windows araçlarına `/x` argümanı verirken `MSYS2_ARG_CONV_EXCL='*'` gerekli.
- `Add-AppxPackage -ExternalLocation <klasör>` o klasörün izinlerini değiştiriyor (yeni alt klasörlere yazılamıyor) →
  geliştirme çıktı klasörüne paket kaydetme; olursa `icacls <klasör> /grant "<kullanıcı>:(OI)(CI)F" /T`.
- Menüyü doğrulamak için: Explorer'ın kullandığı `IContextMenu`/`IExplorerCommand` arayüzleriyle menüyü döküp öğe
  çalıştıran küçük bir C# aracı yaz (ekran görüntüsü yerine). Pencere görüntüsü için `PrintWindow` kullan.
