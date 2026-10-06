// Velopack 1.2.158 hooks must run before loading the Unity Player.
#include <windows.h>
#include "Velopack.h"
extern "C" __declspec(dllexport) DWORD NvOptimusEnablement = 1;
extern "C" __declspec(dllexport) int AmdPowerXpressRequestHighPerformance = 1;
using UnityEntry = int (WINAPI *)(HINSTANCE, HINSTANCE, LPWSTR, int);
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, LPWSTR args, int show) {
    vpkc_app_set_auto_apply_on_startup(false);
    vpkc_app_run(nullptr);
    wchar_t path[32768];
    DWORD length = GetModuleFileNameW(nullptr, path, 32768);
    if (!length || length >= 32768) return 1;
    wchar_t* slash = wcsrchr(path, L'\\');
    if (!slash) return 1;
    wcscpy_s(slash + 1, 32768 - (slash + 1 - path), L"UnityPlayer.dll");
    HMODULE unity = LoadLibraryExW(path, nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!unity) { MessageBoxW(nullptr,L"UnityPlayer.dll is missing or cannot be loaded.",L"Star Racing",MB_ICONERROR); return 1; }
    auto main = reinterpret_cast<UnityEntry>(GetProcAddress(unity,"UnityMain"));
    return main ? main(instance,previous,args,show) : 1;
}
