# File Converter sağ tık eklentisi — REA analizi

Tarih: 2026-10-25
Hedef: `C:\Program Files\File Converter\FileConverterExtension.dll` (kurulu v2.2), SHA-256 `1be6fc37…021d6b`
Yöntem: REA CLI 6.7.0 (MCP sunucusu bağlanamadığı için), yalnızca statik metadata/CIL incelemesi; DLL yüklenmedi, çalıştırılmadı.
Evidence: `inspect_managed_artifact` → `ev_0e206a22…`, `inspect_managed_members` → `ev_95bfd5f0…`

## Neden sorunu
royconvert'in kayıt defteri menüsü (`ExtendedSubCommandsKey`) Explorer'da **toplam 16 öğede kesiliyor** (alt menü başlıkları
ve alt menülerin içi dahil). `IContextMenu` ile menü dökümüyle doğrulandı: .jpg'de 29 öğenin 16'sı, .mp4'te 16'sı görünüyor.
Aynı dökümde File Converter'ın tüm alt menüleri eksiksiz görünüyor.

## Gözlenenler (kanıt: `ev_95bfd5f0…`)
| Konu | Bulgu |
|---|---|
| Teknoloji | .NET Framework, IL-only, x64. Sınıf `SharpShell.SharpContextMenu.SharpContextMenu`'dan türüyor → SharpShell `IShellExtInit` + `IContextMenu` uygular. `IExplorerCommand` referansı **yok** |
| Kayıt | `HKCR\*\shellex\ContextMenuHandlers\FileConverterExtension` = `{AF9B72B5-F4E4-44B0-A3D9-B55B748EFE90}`; COM sunucusu `mscoree.dll`, ThreadingModel `Both` (CLR Explorer'ın içine yükleniyor). Tüm dosya türleri için kayıtlı (`*`); süzme kodda |
| Menüyü gösterme | `CanShowMenu`: seçili dosyaların uzantıları (`Path.GetExtension` → küçük harf, nokta atılmış) önbelleğe alınır; bir preset'in `InputTypes`'ı uzantıyı içeriyorsa menü gösterilir |
| Menü oluşturma | `CreateMenu`: WinForms `ContextMenuStrip` + `ToolStripMenuItem`. Kök öğe "File Converter", preset'in `Folders` dizisine göre iç içe alt menüler (`DropDownItems.Find` ile var olan klasör aranır), her preset bir öğe, ayırıcı, "Configure presets..." (`--settings`). İkonlar gömülü kaynaktan |
| Preset kaynağı | Exe yolu `HKCU\Software\FileConverter\Path`. Ayarlar `%LOCALAPPDATA%`… `Settings.user.xml`, yoksa exe yanındaki `Settings.default.xml` (XmlSerializer). `DisplayPresetIcons` kayıt defterinden |
| Çoklu seçim | `RefreshPresetList`: her preset için eşleşen uzantılar sayılıyor ve uzantı kümesinin boyutuyla karşılaştırılıyor (çıkarım: preset ancak **tüm** seçili uzantıları destekliyorsa listeleniyor) |
| Dosyaları iletme | `ConvertFiles`: `--conversion-preset "<ad>"` + her yol tırnaklı eklenir. Uzunluk kontrolü sonrası yollar `%TEMP%\file-converter-input-list[-{n}].txt` dosyasına yazılıp `--input-files "<liste>"` kullanılıyor; süreç bitince (`Exited`) liste dosyası siliniyor. Tek süreç başlatılıyor |

## Çıkarımlar
- 16 öğe sınırı yalnızca kayıt defterindeki **statik** fiillere uygulanıyor; menüyü kodla üreten bir eklenti (`IContextMenu`) bu sınıra takılmıyor.
- SharpShell/.NET Framework'ü Explorer içinde çalıştırmak Microsoft'un önermediği bir yöntem (Explorer'a CLR yükleniyor). Ayrıca Win11'in modern menüsüne çıkmıyor (yalnızca "Diğer seçenekleri göster").

## royconvert için karar (uygulandı)
- Yerel **C++ DLL** (`src/RoyConvert.Shell`, `royconvert_shell.dll`), CLR yok. Aynı menü ağacı iki COM sınıfıyla sunuluyor:
  - Win 10 klasik menü: `IShellExtInit` + `IContextMenu` (`{34C9CD3A-…}`), `SystemFileAssociations\.ext\shellex\ContextMenuHandlers\royconvert`
  - Win 11 modern menü: `IExplorerCommand` (`{F350E4E0-…}`), sparse paket ile bağlanacak (henüz yapılmadı)
- **Denendi ve elendi:** statik fiil + `ExplorerCommandHandler` Win 10'da yine 16 öğede kesildi (menü dökümüyle doğrulandı).
  Bu yüzden klasik menü için `IContextMenu` şart.
- Menü verisini DLL kendisi üretmez: `royconvert --register-shell` exe yanına `shell-menu.txt` yazar, DLL onu okur (preset listesi C#'ta tek yerde kalır).
- Seçili dosyalar tek süreçle iletilir; çok dosyada `--input-list` (bizde zaten var).
- Kod ve XML kopyalanmadı; yalnızca davranış gözlendi (GPL-3.0).

## Bilinmeyenler
- `COMServerAssociation` özniteliğinin argümanı metadata blob'undan çözülmedi; kayıt defterinden `*` olduğu gözlendi.
- Komut satırı uzunluk eşiğinin tam değeri (CIL sabiti) okunmadı.
