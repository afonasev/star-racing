// Velopack 1.2.158 hooks must run before loading the Unity Player.
#include <windows.h>
#include <string>
#include "Velopack.h"
extern "C" __declspec(dllexport) DWORD NvOptimusEnablement = 1;
extern "C" __declspec(dllexport) int AmdPowerXpressRequestHighPerformance = 1;
static int StartupFailure(const wchar_t* reason, DWORD code) {
    wchar_t systemMessage[1024] = {};
    FormatMessageW(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, nullptr, code, 0, systemMessage, 1024, nullptr);
    std::wstring message = std::wstring(reason) + L"\n\nПопробуйте запустить игру снова. При повторном сбое выполните восстановление через установщик. Пользовательские данные сохранятся.\n\nКод: SR-NATIVE (" + std::to_wstring(code) + L")\n" + systemMessage;
    MessageBoxW(nullptr, message.c_str(), L"Star Racing — ошибка запуска", MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
    return 1;
}
using UnityEntry = int (WINAPI *)(HINSTANCE, HINSTANCE, LPWSTR, int);
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, LPWSTR args, int show) {
    // Velopack lifecycle hooks exit here without loading Unity (also during apply).
    vpkc_app_set_auto_apply_on_startup(false);
    vpkc_app_run(nullptr);
    // Readers retain a handle without owning the mutex. An installer creates
    // and owns it only when no game reader exists; game launch waits if installing.
    HANDLE installGate = CreateMutexW(nullptr, FALSE, L"Global\\StarRacingDesktop-Running");
    if (!installGate) return StartupFailure(L"Не удалось проверить, завершена ли установка игры.", GetLastError());
    DWORD admission = WaitForSingleObject(installGate, 1000);
    if (admission != WAIT_OBJECT_0 && admission != WAIT_ABANDONED) { DWORD code = admission == WAIT_TIMEOUT ? ERROR_BUSY : GetLastError(); CloseHandle(installGate); return StartupFailure(L"Не удалось получить доступ к игре. Дождитесь завершения установки или восстановления.", code); }
    ReleaseMutex(installGate);
    wchar_t path[32768];
    DWORD length = GetModuleFileNameW(nullptr, path, 32768);
    if (!length || length >= 32768) return StartupFailure(L"Не удалось определить папку игры.", length ? ERROR_INSUFFICIENT_BUFFER : GetLastError());
    wchar_t* slash = wcsrchr(path, L'\\');
    if (!slash) return StartupFailure(L"Некорректный путь к игре.", ERROR_BAD_PATHNAME);
    wcscpy_s(slash + 1, 32768 - (slash + 1 - path), L"UnityPlayer.dll");
    // Historical root stubs still target this Player after an in-app update.
    // Route ordinary starts through the new launcher; its explicitly marked child
    // and recovery's token-bound Player must retain their own real process identity.
    wchar_t childFlag[2] = {};
    bool launcherChild = GetEnvironmentVariableW(L"STAR_RACING_LAUNCHER_PLAYER", childFlag, 2) == 1 && childFlag[0] == L'1';
    bool recoveryChild = args && wcsstr(args, L"--star-racing-health-token=");
    std::wstring folder(path, wcsrchr(path, L'\\') - path);
    std::wstring launcher = folder + L"\\StarRacingLauncher.exe";
    DWORD attributes = GetFileAttributesW(launcher.c_str());
    if (!launcherChild && !recoveryChild && attributes != INVALID_FILE_ATTRIBUTES && !(attributes & FILE_ATTRIBUTE_DIRECTORY)) {
        std::wstring command = L"\"" + launcher + L"\" " + (args ? args : L"");
        STARTUPINFOW startup = {}; startup.cb = sizeof(startup);
        PROCESS_INFORMATION process = {};
        if (!CreateProcessW(launcher.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr, folder.c_str(), &startup, &process)) {
            DWORD code = GetLastError(); CloseHandle(installGate); return StartupFailure(L"Не удалось запустить лаунчер Star Racing.", code);
        }
        WaitForSingleObject(process.hProcess, INFINITE);
        DWORD result = 1; GetExitCodeProcess(process.hProcess, &result);
        CloseHandle(process.hThread); CloseHandle(process.hProcess); CloseHandle(installGate);
        return static_cast<int>(result);
    }
    HMODULE unity = LoadLibraryExW(path, nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!unity) return StartupFailure(L"Не удалось загрузить игровой движок UnityPlayer.dll. Проверьте целостность установки и карантин антивируса.", GetLastError());
    auto main = reinterpret_cast<UnityEntry>(GetProcAddress(unity,"UnityMain"));
    if (!main) return StartupFailure(L"Файл игрового движка несовместим с этой версией игры.", GetLastError());
    int result = main(instance,previous,args,show);
    CloseHandle(installGate);
    return result;
}
