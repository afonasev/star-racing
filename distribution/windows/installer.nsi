; NSIS owns installation/shortcuts/uninstall; authenticated agent owns file transactions.
Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"
!include "nsDialogs.nsh"
!define ARP "Software\Microsoft\Windows\CurrentVersion\Uninstall\StarRacingDesktop"
!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
Name "Star Racing ${VERSION}"
OutFile "${OUTPUT}"
InstallDir "$PROGRAMFILES64\Star Racing"
ShowInstDetails show
ShowUninstDetails show
Var Inner
Var OriginalDesktop
Var Params
Var Token
Var Stage
Var Mode
Var InstalledVersion
Var ConfirmedRoot
Var RequestedRoot
Var OriginalRoot
Var Helper
Var PreviousDesktop
Var Prepared

!insertmacro MUI_PAGE_WELCOME
Page custom ExistingPage ExistingLeave
!define MUI_PAGE_CUSTOMFUNCTION_PRE DirectoryPre
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE DirectoryLeave
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Запустить Star Racing"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchGame
!define MUI_FINISHPAGE_SHOWREADME
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Создать ярлык на рабочем столе"
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION CreateDesktopShortcut
; Both finish-page checkboxes are checked by default in the original user process.
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"

Function Inspect
  nsExec::ExecToStack '"$Helper" --inspect-install "$INSTDIR" "${VERSION}"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "$1"
    Abort
  ${EndIf}
  StrCpy $Mode $1 1
  StrCpy $InstalledVersion $1 "" 1
FunctionEnd

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "Для игры требуется 64-разрядная Windows."
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext current
  StrCpy $RequestedRoot $INSTDIR
  StrCpy $OriginalDesktop $DESKTOP
  StrCpy $Inner 0
  StrCpy $Prepared 0
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File "/oname=StarRacingRecovery.exe" "${PAYLOAD}/current/recovery/win-x64/StarRacingRecovery.exe"
  StrCpy $Helper "$PLUGINSDIR\StarRacingRecovery.exe"
  ${GetParameters} $Params
  ReadRegStr $OriginalRoot HKLM "${ARP}" "InstallLocation"
  ReadRegStr $PreviousDesktop HKLM "${ARP}" "DesktopShortcut"
  ClearErrors
  ${GetOptions} $Params "/INNER" $0
  ${IfNot} ${Errors}
    StrCpy $Inner 1
    System::Call 'shell32::IsUserAnAdmin() i .r0'
    ${If} $0 == 0
      Abort
    ${EndIf}
    ${GetOptions} $Params "/DESKTOPPATH=" $OriginalDesktop
    ${GetOptions} $Params "/TOKEN=" $Token
    ; Validate before using a command-line token in any filesystem path.
    nsExec::ExecToStack '"$Helper" --validate-install-token "$Token"'
    Pop $0
    Pop $1
    ${If} $0 != 0
      Abort
    ${EndIf}
    SetSilent silent
  ${Else}
    nsExec::ExecToStack '"$Helper" --new-install-token'
    Pop $0
    Pop $Token
    ${If} $0 != 0
      MessageBox MB_ICONSTOP "Не удалось подготовить установку."
      Abort
    ${EndIf}
  ${EndIf}
  ${If} $OriginalRoot != ""
    ${If} $Inner == 1
    ${AndIf} $RequestedRoot != $OriginalRoot
      MessageBox MB_ICONSTOP "Папка установленной игры изменилась. Запустите мастер заново."
      Abort
    ${EndIf}
    StrCpy $INSTDIR $OriginalRoot
  ${EndIf}
  Call Inspect
FunctionEnd

Function ExistingPage
  ${If} $Mode == "N"
    Abort
  ${EndIf}
  !insertmacro MUI_HEADER_TEXT "Установленная Star Racing" "Проверьте действие перед продолжением."
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${If} $Mode == "R"
    ${NSD_CreateLabel} 0 0 100% 40u "Установлена версия $InstalledVersion.$\r$\nВосстановление версии ${VERSION}: повреждённые и отсутствующие файлы игры будут заменены."
    GetDlgItem $1 $HWNDPARENT 1
    SendMessage $1 ${WM_SETTEXT} 0 "STR:Восстановить"
  ${ElseIf} $Mode == "P"
    ${NSD_CreateLabel} 0 0 100% 48u "Предыдущая установка была прервана.$\r$\nСначала будут восстановлены прежние файлы, затем установлена версия ${VERSION}."
  ${Else}
    ${NSD_CreateLabel} 0 0 100% 40u "Установлена версия $InstalledVersion.$\r$\nВерсия ${VERSION} будет установлена поверх неё."
  ${EndIf}
  ${NSD_CreateLabel} 0 55u 100% 35u "Папка: $INSTDIR"
  ${NSD_CreateLabel} 0 100u 100% 55u "Настройки, рекорды и пользовательские данные сохранятся.$\r$\nЗакройте игру перед продолжением.$\r$\nПри ошибке замены прежние файлы будут восстановлены."
  nsDialogs::Show
FunctionEnd

Function ExistingLeave
  StrCpy $ConfirmedRoot $INSTDIR
FunctionEnd

Function DirectoryPre
  ${If} $Mode != "N"
    Abort
  ${EndIf}
FunctionEnd
Function DirectoryLeave
  ${If} $OriginalRoot != ""
    StrCpy $INSTDIR $OriginalRoot
  ${EndIf}
  Call Inspect
  ${If} $Inner == 0
  ${AndIf} $Mode != "N"
  ${AndIf} $ConfirmedRoot != $INSTDIR
    MessageBox MB_YESNO|MB_ICONQUESTION "В папке $INSTDIR обнаружена Star Racing $InstalledVersion. Восстановить/установить версию ${VERSION} с сохранением пользовательских данных?" IDYES confirm_directory
    Abort
confirm_directory:
    StrCpy $ConfirmedRoot $INSTDIR
  ${EndIf}
FunctionEnd

Function CreateDesktopShortcut
  SetShellVarContext current
  ClearErrors
  CreateShortCut "$DESKTOP\Star Racing.lnk" "$INSTDIR\Star Racing.exe" "" "$INSTDIR\Star Racing.ico" 0
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "Не удалось создать ярлык на рабочем столе. Проверьте доступ к папке рабочего стола."
  ${EndIf}
FunctionEnd
Function LaunchGame
  SetOutPath "$INSTDIR"
  Exec '"$INSTDIR\Star Racing.exe"'
FunctionEnd

Section "Install"
  Call DirectoryLeave
  ${If} $Inner == 0
    ClearErrors
    ExecShellWait "runas" "$EXEPATH" '/S /INNER /TOKEN=$Token /DESKTOPPATH=$\"$OriginalDesktop$\" /D=$INSTDIR'
    ${If} ${Errors}
      MessageBox MB_ICONSTOP "Установка отменена или не удалось получить права администратора."
      Abort
    ${EndIf}
    ClearErrors
    FileOpen $0 "$INSTDIR\installer.receipt" r
    ${IfNot} ${Errors}
      FileRead $0 $1
      FileClose $0
    ${Else}
      StrCpy $1 ""
    ${EndIf}
    ${If} $1 != $Token
      MessageBox MB_ICONSTOP "Установка не завершена. Игра не будет запущена. Повторите установку после закрытия игры."
      Abort
    ${EndIf}
  ${Else}
    StrCpy $Stage "$INSTDIR.sr-stage-$Token"
    IfFileExists "$Stage\*" 0 +3
      MessageBox MB_ICONSTOP "Временная папка уже существует. Установка остановлена без изменения файлов."
      Abort
    SetOutPath "$Stage"
    ClearErrors
    File /r "${PAYLOAD}/*"
    File "/oname=Star Racing.ico" "${ICON}"
    WriteUninstaller "$Stage\Uninstall-Star-Racing.exe"
    FileOpen $0 "$Stage\installer.complete" w
    FileWrite $0 "StarRacingDesktop-v1"
    FileClose $0
    ${If} ${Errors}
      SetOutPath "$PLUGINSDIR"
      RMDir /r "$Stage"
      Abort
    ${EndIf}
    ; No process may retain the staging working directory while the agent moves it.
    SetOutPath "$PLUGINSDIR"
    nsExec::ExecToStack '"$Helper" --prepare-install "$Stage" "$INSTDIR" "${VERSION}" "$Token"'
    Pop $0
    Pop $1
    ${If} $0 != 0
      ; A failed rollback retains journal/backup for the next run; do not delete them.
      MessageBox MB_ICONSTOP "$1"
      Abort
    ${EndIf}
    StrCpy $InstalledVersion $1 "" 1
    StrCpy $Prepared 1
    SetShellVarContext all
    ClearErrors
    WriteRegStr HKLM "${ARP}" "DisplayName" "Star Racing"
    WriteRegStr HKLM "${ARP}" "DisplayVersion" "${VERSION}"
    WriteRegStr HKLM "${ARP}" "Publisher" "Afonasev"
    WriteRegStr HKLM "${ARP}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKLM "${ARP}" "DisplayIcon" "$INSTDIR\Star Racing.ico"
    WriteRegStr HKLM "${ARP}" "DesktopShortcut" "$OriginalDesktop\Star Racing.lnk"
    WriteRegStr HKLM "${ARP}" "UninstallString" '$\"$INSTDIR\Uninstall-Star-Racing.exe$\"'
    WriteRegDWORD HKLM "${ARP}" "NoModify" 1
    ; ARP offers uninstall; repair is explicitly offered by this signed installer.
    WriteRegDWORD HKLM "${ARP}" "NoRepair" 1
    CreateShortCut "$SMPROGRAMS\Star Racing.lnk" "$INSTDIR\Star Racing.exe" "" "$INSTDIR\Star Racing.ico" 0
    ${If} ${Errors}
      Call RollbackWorker
      Abort
    ${EndIf}
    nsExec::ExecToStack '"$Helper" --commit-install "$INSTDIR" "$Token"'
    Pop $0
    Pop $1
    ${If} $0 != 0
      Call RollbackWorker
      MessageBox MB_ICONSTOP "$1"
      Abort
    ${EndIf}
    StrCpy $Prepared 0
  ${EndIf}
SectionEnd

Function RollbackWorker
  ${If} $Prepared == 1
    nsExec::ExecToStack '"$Helper" --rollback-install "$INSTDIR" "$Token"'
    Pop $2
    Pop $3
    ${If} $2 == 0
      StrCpy $Prepared 0
      ${If} $OriginalRoot == ""
        DeleteRegKey HKLM "${ARP}"
        Delete "$SMPROGRAMS\Star Racing.lnk"
      ${Else}
        WriteRegStr HKLM "${ARP}" "DisplayVersion" "$InstalledVersion"
        WriteRegStr HKLM "${ARP}" "DesktopShortcut" "$PreviousDesktop"
      ${EndIf}
    ${Else}
      MessageBox MB_ICONSTOP "Восстановление не завершено: $3. Сохранены журнал и резервные файлы; закройте игру и повторите установку."
    ${EndIf}
  ${EndIf}
FunctionEnd
Function .onUserAbort
  Call RollbackWorker
FunctionEnd

Function un.onInit
  SetRegView 64
  System::Call 'shell32::IsUserAnAdmin() i .r0'
  ${If} $0 == 0
    ClearErrors
    ExecShellWait "runas" "$EXEPATH" '_?=$INSTDIR'
    ${If} ${Errors}
      MessageBox MB_ICONSTOP "Удаление отменено: нужны права администратора."
    ${EndIf}
    Quit
  ${EndIf}
FunctionEnd
Section "Uninstall"
  SetShellVarContext all
  SetOutPath "$TEMP"
  ; Copy the agent before deleting its own installed directory.
  InitPluginsDir
  CopyFiles /SILENT "$INSTDIR\current\recovery\win-x64\StarRacingRecovery.exe" "$PLUGINSDIR\StarRacingRecovery.exe"
  nsExec::ExecToStack '"$PLUGINSDIR\StarRacingRecovery.exe" --remove-install "$INSTDIR"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Не удалось удалить игру: $1. Пользовательские данные сохранены."
    Abort
  ${EndIf}
  ReadRegStr $0 HKLM "${ARP}" "DesktopShortcut"
  ${If} $0 != ""
    Delete "$0"
  ${EndIf}
  Delete "$DESKTOP\Star Racing.lnk"
  Delete "$INSTDIR\Star Racing.ico"
  Delete "$SMPROGRAMS\Star Racing.lnk"
  Delete "$INSTDIR\installer.complete"
  Delete "$INSTDIR\installer.receipt"
  Delete "$INSTDIR\Uninstall-Star-Racing.exe"
  DeleteRegKey HKLM "${ARP}"
  RMDir "$INSTDIR"
SectionEnd
