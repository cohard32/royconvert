// royconvert Explorer context menu.
//
// One menu tree, offered through two COM classes:
//   ContextMenu (IShellExtInit + IContextMenu): the classic menu, registered under
//     HKCU\Software\Classes\SystemFileAssociations\.ext\shellex\ContextMenuHandlers\royconvert.
//     It builds real popup menus, so it is not cut off after 16 items like registry cascades
//     (static verbs with ExplorerCommandHandler hit the same limit on Windows 10).
//   RootCommand (IExplorerCommand): the Windows 11 menu, declared by the sparse package.
//
// The menu comes from %LOCALAPPDATA%\royconvert\shell-menu.txt (per user, in that user's language; the install folder
// may not be writable), written by `royconvert --register-shell`:
//   <preset id> TAB <label, "Group/Item" for a submenu> TAB <space separated input extensions>
// DVD presets have "<dvd>" instead of extensions; they are offered when every selected item is a DVD folder or drive
// (the classic menu is also registered for Directory and Drive).
// Choosing an item starts royconvert.exe (also next to this DLL) once for the whole selection.

#define NOMINMAX
#include <windows.h>
#include <shellapi.h>
#include <shlobj_core.h>
#include <shlwapi.h>
#include <shobjidl_core.h>
#include <wrl/module.h>
#include <wrl/implements.h>
#include <wrl/client.h>

#include <algorithm>
#include <fstream>
#include <memory>
#include <set>
#include <sstream>
#include <string>
#include <vector>

using Microsoft::WRL::ClassicCom;
using Microsoft::WRL::ComPtr;
using Microsoft::WRL::InhibitRoOriginateError;
using Microsoft::WRL::Make;
using Microsoft::WRL::RuntimeClass;
using Microsoft::WRL::RuntimeClassFlags;

namespace
{
    HMODULE g_module = nullptr;

    // Longer command lines go through a --temp-input-list file (CreateProcess allows 32767 characters).
    constexpr size_t MaxCommandLine = 30000;

    // The "extension" of DVD presets in the menu file.
    constexpr wchar_t DvdInput[] = L"<dvd>";

    struct Preset
    {
        std::wstring id;
        std::wstring label;
        std::set<std::wstring> extensions;
    };

    struct MenuNode
    {
        std::wstring title;
        std::wstring presetId; // empty for a submenu
        std::vector<std::shared_ptr<MenuNode>> children;
    };

    std::wstring ModuleDirectory()
    {
        wchar_t path[MAX_PATH * 4];
        const DWORD length = GetModuleFileNameW(g_module, path, ARRAYSIZE(path));
        std::wstring result(path, length);
        return result.substr(0, result.find_last_of(L'\\') + 1);
    }

    std::wstring ExePath() { return ModuleDirectory() + L"royconvert.exe"; }

    std::wstring Utf8ToWide(const std::string& text)
    {
        if (text.empty())
        {
            return {};
        }

        const int length = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
        std::wstring result(length, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), result.data(), length);
        return result;
    }

    std::wstring Lower(std::wstring text)
    {
        CharLowerBuffW(text.data(), static_cast<DWORD>(text.size()));
        return text;
    }

    std::wstring MenuFilePath()
    {
        PWSTR folder = nullptr;
        std::wstring path;
        if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &folder)))
        {
            path = std::wstring(folder) + L"\\royconvert\\shell-menu.txt";
        }

        CoTaskMemFree(folder);
        return path;
    }

    std::vector<Preset> LoadPresets()
    {
        std::vector<Preset> presets;
        std::ifstream file(MenuFilePath(), std::ios::binary);
        std::string line;
        while (std::getline(file, line))
        {
            if (!line.empty() && line.back() == '\r')
            {
                line.pop_back();
            }

            std::wstringstream fields(Utf8ToWide(line));
            Preset preset;
            std::wstring extensions;
            if (!std::getline(fields, preset.id, L'\t') || !std::getline(fields, preset.label, L'\t') || !std::getline(fields, extensions))
            {
                continue;
            }

            std::wstringstream words(Lower(extensions));
            for (std::wstring extension; words >> extension;)
            {
                preset.extensions.insert(extension);
            }

            presets.push_back(std::move(preset));
        }

        return presets;
    }

    std::vector<std::wstring> SelectedPaths(IShellItemArray* items)
    {
        std::vector<std::wstring> paths;
        DWORD count = 0;
        if (items == nullptr || FAILED(items->GetCount(&count)))
        {
            return paths;
        }

        for (DWORD i = 0; i < count; i++)
        {
            ComPtr<IShellItem> item;
            PWSTR path = nullptr;
            if (SUCCEEDED(items->GetItemAt(i, &item)) && SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)))
            {
                paths.emplace_back(path);
                CoTaskMemFree(path);
            }
        }

        return paths;
    }

    bool IsDirectory(const std::wstring& path)
    {
        const DWORD attributes = GetFileAttributesW(path.c_str());
        return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
    }

    /// A DVD-Video folder, as royconvert's DvdDisc.Open finds it: a drive root or folder containing VIDEO_TS, or
    /// VIDEO_TS itself, with at least one title VOB (VTS_nn_1..9.VOB; VTS_nn_0 is the menu).
    bool IsDvd(std::wstring path)
    {
        // An empty floppy or card reader drive must not show "insert a disk".
        const UINT previousMode = GetErrorMode();
        SetThreadErrorMode(previousMode | SEM_FAILCRITICALERRORS, nullptr);

        if (!path.empty() && path.back() == L'\\')
        {
            path.pop_back();
        }

        if (Lower(PathFindFileNameW(path.c_str())) != L"video_ts")
        {
            path += L"\\VIDEO_TS";
        }

        bool found = false;
        WIN32_FIND_DATAW file;
        HANDLE search = FindFirstFileExW((path + L"\\VTS_??_?.VOB").c_str(), FindExInfoBasic, &file, FindExSearchNameMatch, nullptr, 0);
        if (search != INVALID_HANDLE_VALUE)
        {
            do
            {
                const std::wstring name = file.cFileName;
                found = (file.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0 && name.size() == 12 && name[7] >= L'1' && name[7] <= L'9';
            } while (!found && FindNextFileW(search, &file));

            FindClose(search);
        }

        SetThreadErrorMode(previousMode, nullptr);
        return found;
    }

    /// Presets that accept every selected item, as a tree: "Scale 75%/PNG" goes under "Scale 75%".
    /// Files are matched by extension; folders and drives only by DVD presets, and only when each is a DVD.
    std::shared_ptr<MenuNode> BuildMenu(const std::vector<std::wstring>& paths)
    {
        auto root = std::make_shared<MenuNode>();
        root->title = L"royconvert";
        if (paths.empty())
        {
            return root;
        }

        std::set<std::wstring> extensions;
        for (const auto& path : paths)
        {
            if (IsDirectory(path))
            {
                if (!IsDvd(path))
                {
                    return root;
                }

                extensions.insert(DvdInput);
                continue;
            }

            const wchar_t* extension = PathFindExtensionW(path.c_str());
            if (*extension != L'.')
            {
                return root; // a file without extension
            }

            extensions.insert(Lower(extension + 1));
        }

        for (const auto& preset : LoadPresets())
        {
            if (!std::includes(preset.extensions.begin(), preset.extensions.end(), extensions.begin(), extensions.end()))
            {
                continue;
            }

            auto parent = root;
            std::wstring label = preset.label;
            const size_t slash = label.find(L'/');
            if (slash != std::wstring::npos)
            {
                const std::wstring group = label.substr(0, slash);
                label = label.substr(slash + 1);
                auto existing = std::find_if(root->children.begin(), root->children.end(),
                    [&](const auto& child) { return child->presetId.empty() && child->title == group; });
                if (existing == root->children.end())
                {
                    auto submenu = std::make_shared<MenuNode>();
                    submenu->title = group;
                    root->children.push_back(submenu);
                    parent = submenu;
                }
                else
                {
                    parent = *existing;
                }
            }

            auto leaf = std::make_shared<MenuNode>();
            leaf->title = label;
            leaf->presetId = preset.id;
            parent->children.push_back(leaf);
        }

        return root;
    }

    std::wstring Quote(const std::wstring& argument)
    {
        // Backslashes before the closing quote must be doubled: "E:\" would be read as E:" (a drive root).
        const size_t trailing = argument.size() - argument.find_last_not_of(L'\\') - 1;
        return L"\"" + argument + std::wstring(trailing, L'\\') + L"\"";
    }

    HRESULT StartConversion(const std::wstring& presetId, const std::vector<std::wstring>& paths)
    {
        std::wstring commandLine = Quote(ExePath()) + L" --preset " + presetId;
        size_t length = commandLine.size();
        for (const auto& path : paths)
        {
            length += path.size() + 3;
        }

        if (length < MaxCommandLine)
        {
            for (const auto& path : paths)
            {
                commandLine += L" " + Quote(path);
            }
        }
        else
        {
            // royconvert reads the list while parsing its arguments, then deletes it.
            wchar_t directory[MAX_PATH];
            wchar_t listPath[MAX_PATH];
            if (GetTempPathW(MAX_PATH, directory) == 0 || GetTempFileNameW(directory, L"rcv", 0, listPath) == 0)
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }

            std::ofstream list(listPath, std::ios::binary | std::ios::trunc);
            for (const auto& path : paths)
            {
                const int size = WideCharToMultiByte(CP_UTF8, 0, path.c_str(), static_cast<int>(path.size()), nullptr, 0, nullptr, nullptr);
                std::string utf8(size, '\0');
                WideCharToMultiByte(CP_UTF8, 0, path.c_str(), static_cast<int>(path.size()), utf8.data(), size, nullptr, nullptr);
                list << utf8 << "\r\n";
            }

            commandLine += L" --temp-input-list " + Quote(listPath);
        }

        STARTUPINFOW startup{sizeof(startup)};
        PROCESS_INFORMATION process{};
        if (!CreateProcessW(nullptr, commandLine.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup, &process))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        // Lets royconvert bring its window to the front.
        AllowSetForegroundWindow(process.dwProcessId);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return S_OK;
    }

    class CommandEnumerator final : public RuntimeClass<RuntimeClassFlags<ClassicCom | InhibitRoOriginateError>, IEnumExplorerCommand>
    {
    public:
        explicit CommandEnumerator(std::vector<ComPtr<IExplorerCommand>> commands) : _commands(std::move(commands)) {}

        IFACEMETHODIMP Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override
        {
            ULONG done = 0;
            for (; done < count && _position < _commands.size(); done++)
            {
                _commands[_position++].CopyTo(&commands[done]);
            }

            if (fetched != nullptr)
            {
                *fetched = done;
            }

            return done == count ? S_OK : S_FALSE;
        }

        IFACEMETHODIMP Skip(ULONG count) override
        {
            _position = std::min(_position + count, _commands.size());
            return _position < _commands.size() ? S_OK : S_FALSE;
        }

        IFACEMETHODIMP Reset() override
        {
            _position = 0;
            return S_OK;
        }

        IFACEMETHODIMP Clone(IEnumExplorerCommand** result) override
        {
            *result = nullptr;
            return E_NOTIMPL;
        }

    private:
        std::vector<ComPtr<IExplorerCommand>> _commands;
        size_t _position = 0;
    };

    class MenuCommand : public RuntimeClass<RuntimeClassFlags<ClassicCom | InhibitRoOriginateError>, IExplorerCommand, IObjectWithSite>
    {
    public:
        MenuCommand() = default;
        explicit MenuCommand(std::shared_ptr<MenuNode> node) : _node(std::move(node)) {}

        IFACEMETHODIMP GetTitle(IShellItemArray* items, PWSTR* name) override
        {
            return SHStrDupW(Node(items)->title.c_str(), name);
        }

        IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override
        {
            if (!IsRoot())
            {
                *icon = nullptr;
                return E_NOTIMPL;
            }

            return SHStrDupW((ExePath() + L",0").c_str(), icon);
        }

        IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tip) override
        {
            *tip = nullptr;
            return E_NOTIMPL;
        }

        IFACEMETHODIMP GetCanonicalName(GUID* name) override
        {
            *name = GUID_NULL;
            return E_NOTIMPL;
        }

        IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
        {
            // Explorer asks the root for its state with the selection before enumerating subcommands, which get no items.
            if (IsRoot())
            {
                _selection = SelectedPaths(items);
                _root = BuildMenu(_selection);
                *state = _root->children.empty() ? ECS_HIDDEN : ECS_ENABLED;
                return S_OK;
            }

            *state = ECS_ENABLED;
            return S_OK;
        }

        IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
        {
            if (_node == nullptr || _node->presetId.empty())
            {
                return E_NOTIMPL;
            }

            const auto paths = items != nullptr ? SelectedPaths(items) : _selection;
            return paths.empty() ? E_INVALIDARG : StartConversion(_node->presetId, paths);
        }

        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
        {
            *flags = IsRoot() || _node->presetId.empty() ? ECF_HASSUBCOMMANDS : ECF_DEFAULT;
            return S_OK;
        }

        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
        {
            *commands = nullptr;
            const auto node = IsRoot() ? _root : _node;
            if (node == nullptr)
            {
                return E_NOTIMPL;
            }

            std::vector<ComPtr<IExplorerCommand>> children;
            for (const auto& child : node->children)
            {
                auto command = Make<MenuCommand>(child);
                command->_selection = _selection;
                children.push_back(command);
            }

            return Make<CommandEnumerator>(std::move(children)).CopyTo(commands);
        }

        IFACEMETHODIMP SetSite(IUnknown* site) override
        {
            _site = site;
            return S_OK;
        }

        IFACEMETHODIMP GetSite(REFIID riid, void** site) override
        {
            return _site ? _site.CopyTo(riid, site) : E_FAIL;
        }

    private:
        bool IsRoot() const { return _node == nullptr; }

        const MenuNode* Node(IShellItemArray* items)
        {
            if (!IsRoot())
            {
                return _node.get();
            }

            if (_root == nullptr)
            {
                _selection = SelectedPaths(items);
                _root = BuildMenu(_selection);
            }

            return _root.get();
        }

        std::shared_ptr<MenuNode> _node; // null for the root command that Explorer creates
        std::shared_ptr<MenuNode> _root;
        std::vector<std::wstring> _selection;
        ComPtr<IUnknown> _site;
    };

    class __declspec(uuid("F350E4E0-298E-47B5-9E43-F82D43D99A06")) RootCommand final : public MenuCommand
    {
    };

    /// Small icon of royconvert.exe as a 32-bit bitmap for menu items; null if the exe has no icon.
    HBITMAP LoadMenuBitmap()
    {
        HICON icon = nullptr;
        if (ExtractIconExW(ExePath().c_str(), 0, nullptr, &icon, 1) == 0 || icon == nullptr)
        {
            return nullptr;
        }

        const int width = GetSystemMetrics(SM_CXSMICON);
        const int height = GetSystemMetrics(SM_CYSMICON);
        BITMAPINFO info{};
        info.bmiHeader.biSize = sizeof(info.bmiHeader);
        info.bmiHeader.biWidth = width;
        info.bmiHeader.biHeight = -height;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = BI_RGB;

        void* bits = nullptr;
        HDC dc = CreateCompatibleDC(nullptr);
        HBITMAP bitmap = CreateDIBSection(dc, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
        if (bitmap != nullptr)
        {
            HGDIOBJ previous = SelectObject(dc, bitmap);
            DrawIconEx(dc, 0, 0, icon, width, height, 0, nullptr, DI_NORMAL);
            SelectObject(dc, previous);
        }

        DeleteDC(dc);
        DestroyIcon(icon);
        return bitmap;
    }

    class __declspec(uuid("34C9CD3A-4231-4BBA-B15E-9C1DF9B05DB2")) ContextMenu final
        : public RuntimeClass<RuntimeClassFlags<ClassicCom | InhibitRoOriginateError>, IShellExtInit, IContextMenu>
    {
    public:
        ~ContextMenu() override
        {
            if (_bitmap != nullptr)
            {
                DeleteObject(_bitmap);
            }
        }

        IFACEMETHODIMP Initialize(PCIDLIST_ABSOLUTE, IDataObject* data, HKEY) override
        {
            _selection.clear();
            if (data == nullptr)
            {
                return E_INVALIDARG;
            }

            FORMATETC format{CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
            STGMEDIUM medium{};
            if (FAILED(data->GetData(&format, &medium)))
            {
                return E_INVALIDARG;
            }

            if (auto drop = static_cast<HDROP>(GlobalLock(medium.hGlobal)))
            {
                const UINT count = DragQueryFileW(drop, 0xFFFFFFFF, nullptr, 0);
                for (UINT i = 0; i < count; i++)
                {
                    std::wstring path(DragQueryFileW(drop, i, nullptr, 0), L'\0');
                    DragQueryFileW(drop, i, path.data(), static_cast<UINT>(path.size() + 1));
                    _selection.push_back(std::move(path));
                }

                GlobalUnlock(medium.hGlobal);
            }

            ReleaseStgMedium(&medium);
            return S_OK;
        }

        IFACEMETHODIMP QueryContextMenu(HMENU menu, UINT index, UINT firstId, UINT lastId, UINT flags) override
        {
            _presetIds.clear();
            const auto root = (flags & CMF_DEFAULTONLY) ? nullptr : BuildMenu(_selection);
            if (root == nullptr || root->children.empty())
            {
                return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, 0);
            }

            if (_bitmap == nullptr)
            {
                _bitmap = LoadMenuBitmap();
            }

            HMENU popup = CreatePopupMenu();
            UINT nextId = firstId;
            AddItems(popup, *root, firstId, nextId, lastId);
            InsertItem(menu, index, root->title, nextId++, popup, _bitmap);
            _presetIds.emplace_back();
            return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, nextId - firstId);
        }

        IFACEMETHODIMP InvokeCommand(CMINVOKECOMMANDINFO* info) override
        {
            // Our items have no verb names, only offsets.
            if (!IS_INTRESOURCE(info->lpVerb))
            {
                return E_FAIL;
            }

            const UINT offset = LOWORD(info->lpVerb);
            if (offset >= _presetIds.size() || _presetIds[offset].empty())
            {
                return E_FAIL;
            }

            return StartConversion(_presetIds[offset], _selection);
        }

        IFACEMETHODIMP GetCommandString(UINT_PTR, UINT, UINT*, CHAR*, UINT) override { return E_NOTIMPL; }

    private:
        void AddItems(HMENU menu, const MenuNode& node, UINT firstId, UINT& nextId, UINT lastId)
        {
            UINT position = 0;
            for (const auto& child : node.children)
            {
                if (nextId >= lastId)
                {
                    return; // keep one id for the root item
                }

                HMENU submenu = nullptr;
                if (child->presetId.empty())
                {
                    submenu = CreatePopupMenu();
                    AddItems(submenu, *child, firstId, nextId, lastId);
                }

                _presetIds.resize(nextId - firstId + 1);
                _presetIds[nextId - firstId] = child->presetId;
                InsertItem(menu, position++, child->title, nextId++, submenu, nullptr);
            }
        }

        static void InsertItem(HMENU menu, UINT position, const std::wstring& title, UINT id, HMENU submenu, HBITMAP bitmap)
        {
            MENUITEMINFOW item{sizeof(item)};
            item.fMask = MIIM_STRING | MIIM_ID | (submenu != nullptr ? MIIM_SUBMENU : 0) | (bitmap != nullptr ? MIIM_BITMAP : 0);
            item.wID = id;
            item.hSubMenu = submenu;
            item.hbmpItem = bitmap;
            item.dwTypeData = const_cast<LPWSTR>(title.c_str());
            InsertMenuItemW(menu, position, TRUE, &item);
        }

        std::vector<std::wstring> _selection;
        std::vector<std::wstring> _presetIds; // by command offset; empty for submenus
        HBITMAP _bitmap = nullptr;
    };

    CoCreatableClass(RootCommand)
    CoCreatableClass(ContextMenu)
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** result)
{
    return Microsoft::WRL::Module<Microsoft::WRL::InProc>::GetModule().GetClassObject(clsid, riid, result);
}

STDAPI DllCanUnloadNow()
{
    return Microsoft::WRL::Module<Microsoft::WRL::InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }

    return TRUE;
}
