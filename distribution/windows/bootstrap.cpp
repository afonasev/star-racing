// Velopack 1.2.158 hooks must run before loading the Unity Player.
#include <windows.h>
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
    HMODULE unity = LoadLibraryExW(path, nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!unity) { MessageBoxW(nullptr,L"UnityPlayer.dll is missing or cannot be loaded.",L"Star Racing",MB_ICONERROR); return 1; }
    auto main = reinterpret_cast<UnityEntry>(GetProcAddress(unity,"UnityMain"));
    int result = main ? main(instance,previous,args,show) : 1;
    CloseHandle(installGate);
    return result;
}
