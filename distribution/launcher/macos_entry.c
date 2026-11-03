// Universal .app entry; the managed launcher and Unity remain separate processes.
#include <mach-o/dyld.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
int main(int argc, char **argv) {
    char path[PATH_MAX], canonical[PATH_MAX]; uint32_t size = sizeof(path);
    if (_NSGetExecutablePath(path, &size) || !realpath(path, canonical)) return 1;
    char *slash = strrchr(canonical, '/'); if (!slash) return 1; *slash = 0;
#if defined(__arm64__)
    const char *runtime = "osx-arm64";
#else
    const char *runtime = "osx-x64";
#endif
    char launcher[PATH_MAX];
    if (snprintf(launcher, sizeof(launcher), "%s/launcher/%s/StarRacingLauncher", canonical, runtime) >= sizeof(launcher)) return 1;
    argv[0] = launcher;
    execv(launcher, argv);
    perror("Star Racing launcher"); return 1;
}
