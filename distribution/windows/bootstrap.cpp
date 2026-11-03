// Velopack 1.2.158 hooks must run before loading the Unity Player.
#include <windows.h>
#include <string>
#include "Velopack.h"
extern "C" __declspec(dllexport) DWORD NvOptimusEnablement = 1;
extern "C" __declspec(dllexport) int AmdPowerXpressRequestHighPerformance = 1;
using UnityEntry = int (WINAPI *)(HINSTANCE, HINSTANCE, LPWSTR, int);
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, LPWSTR args, int show) {
    // Velopack lifecycle hooks exit here without loading Unity (also during apply).
    vpkc_app_set_auto_apply_on_startup(false);
    vpkc_app_run(nullptr);
    // Readers retain a handle without owning the mutex. An installer creates
    // and owns it only when no game reader exists; game launch waits if installing.
    HANDLE installGate = CreateMutexW(nullptr, FALSE, L"Global\\StarRacingDesktop-Running");
    if (!installGate) return 1;
    DWORD admission = WaitForSingleObject(installGate, 1000);
    if (admission != WAIT_OBJECT_0 && admission != WAIT_ABANDONED) { CloseHandle(installGate); return 1; }
    ReleaseMutex(installGate);
    wchar_t path[32768];
    DWORD length = GetModuleFileNameW(nullptr, path, 32768);
    if (!length || length >= 32768) return 1;
    wchar_t* slash = wcsrchr(path, L'\\');
    if (!slash) return 1;
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
            CloseHandle(installGate); return 1;
        }
        WaitForSingleObject(process.hProcess, INFINITE);
        DWORD result = 1; GetExitCodeProcess(process.hProcess, &result);
        CloseHandle(process.hThread); CloseHandle(process.hProcess); CloseHandle(installGate);
        return static_cast<int>(result);
    }
    HMODULE unity = LoadLibraryExW(path, nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!unity) { MessageBoxW(nullptr,L"UnityPlayer.dll is missing or cannot be loaded.",L"Star Racing",MB_ICONERROR); return 1; }
    auto main = reinterpret_cast<UnityEntry>(GetProcAddress(unity,"UnityMain"));
    int result = main ? main(instance,previous,args,show) : 1;
    CloseHandle(installGate);
    return result;
}
