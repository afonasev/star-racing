; NSIS owns installation/shortcuts/uninstall; Velopack owns consent-based updates.
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

!insertmacro MUI_PAGE_WELCOME
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE DirectoryLeave
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Запустить Star Racing"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchGame
; MUI_FINISHPAGE_RUN_NOTCHECKED is intentionally absent: checked by default.
!define MUI_FINISHPAGE_SHOWREADME
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Создать ярлык на рабочем столе"
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION CreateDesktopShortcut
; Both finish-page checkboxes are checked by default.
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "Для игры требуется 64-разрядная Windows."
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext current
  StrCpy $OriginalDesktop $DESKTOP
  StrCpy $Inner 0
  ${GetParameters} $Params
  ClearErrors
  ${GetOptions} $Params "/INNER" $0
  ${IfNot} ${Errors}
    StrCpy $Inner 1
    System::Call 'shell32::IsUserAnAdmin() i .r0'
    ${If} $0 == 0
      Abort
    ${EndIf}
    ${GetOptions} $Params "/DESKTOPPATH=" $OriginalDesktop
    SetSilent silent
  ${Else}
    ReadRegStr $0 HKLM "${ARP}" "InstallLocation"
    ${If} $0 != ""
      MessageBox MB_ICONSTOP "Star Racing уже установлена в $0. Обновляйте игру из главного меню. Для смены папки сначала удалите установленную копию."
      Abort
    ${EndIf}
  ${EndIf}
FunctionEnd

; Never silently repair/overwrite an unrelated nonempty directory.
Function DirectoryLeave
  FindFirst $0 $1 "$INSTDIR\*"
check_entry:
  ${If} $1 == ""
    FindClose $0
    Return
  ${EndIf}
  ${If} $1 != "."
  ${AndIf} $1 != ".."
    FindClose $0
    MessageBox MB_ICONSTOP "Выберите пустую папку. Уже установленная игра обновляется из главного меню."
    Abort
  ${EndIf}
  FindNext $0 $1
  Goto check_entry
FunctionEnd

Function CreateDesktopShortcut
  ; This callback runs in the original unelevated wizard, after installation.
  ; Windows resolves the user's redirected/OneDrive Desktop for this account.
  SetShellVarContext current
  ClearErrors
  CreateShortCut "$DESKTOP\Star Racing.lnk" "$INSTDIR\Star Racing.exe" "" "$INSTDIR\Star Racing.ico" 0
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "Не удалось создать ярлык на рабочем столе. Проверьте доступ к папке рабочего стола."
  ${EndIf}
FunctionEnd

Function LaunchGame
  ; Outer wizard is asInvoker, including after alternate-admin UAC credentials.
  SetOutPath "$INSTDIR"
  Exec '"$INSTDIR\Star Racing.exe"'
FunctionEnd

Section "Install"
  ${If} $Inner == 0
    Call DirectoryLeave
    ClearErrors
    ExecShellWait "runas" "$EXEPATH" '/S /INNER /DESKTOPPATH=$\"$OriginalDesktop$\" /D=$INSTDIR'
    ${If} ${Errors}
      MessageBox MB_ICONSTOP "Установка отменена или не удалось получить права администратора."
      Abort
    ${EndIf}
    ; Folder was empty: this marker exists only after the entire worker succeeds.
    IfFileExists "$INSTDIR\installer.complete" +3
      MessageBox MB_ICONSTOP "Установка не завершена. Игра не будет запущена."
      Abort
    FileOpen $0 "$INSTDIR\installer.complete" r
    FileRead $0 $1
    FileClose $0
    ${If} $1 != "StarRacingDesktop-v1"
      MessageBox MB_ICONSTOP "Не удалось подтвердить завершение установки."
      Abort
    ${EndIf}
  ${Else}
    Call DirectoryLeave
    SetShellVarContext all
    SetOutPath "$INSTDIR"
    ClearErrors
    File /r "${PAYLOAD}/*"
    File "/oname=Star Racing.ico" "${ICON}"
    WriteUninstaller "$INSTDIR\Uninstall-Star-Racing.exe"
    WriteRegStr HKLM "${ARP}" "DisplayName" "Star Racing"
    WriteRegStr HKLM "${ARP}" "Publisher" "Afonasev"
    WriteRegStr HKLM "${ARP}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKLM "${ARP}" "DisplayIcon" "$INSTDIR\Star Racing.ico"
    WriteRegStr HKLM "${ARP}" "DesktopShortcut" "$OriginalDesktop\Star Racing.lnk"
    WriteRegStr HKLM "${ARP}" "UninstallString" '$\"$INSTDIR\Uninstall-Star-Racing.exe$\"'
    WriteRegDWORD HKLM "${ARP}" "NoModify" 1
    WriteRegDWORD HKLM "${ARP}" "NoRepair" 1
    CreateShortCut "$SMPROGRAMS\Star Racing.lnk" "$INSTDIR\Star Racing.exe" "" "$INSTDIR\Star Racing.ico" 0
    ${If} ${Errors}
      Abort
    ${EndIf}
    FileOpen $0 "$INSTDIR\installer.complete" w
    FileWrite $0 "StarRacingDesktop-v1"
    FileClose $0
  ${EndIf}
SectionEnd

Function un.onInit
  SetRegView 64
  System::Call 'shell32::IsUserAnAdmin() i .r0'
  ${If} $0 == 0
    ClearErrors
    ; Preserve the installation root when the uninstaller runs from its temp copy.
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
  ; Only our dedicated payload directories/files, never arbitrary install-root data.
  ClearErrors
  IfFileExists "$INSTDIR\current\*" 0 +2
    RMDir /r "$INSTDIR\current"
  IfFileExists "$INSTDIR\packages\*" 0 +2
    RMDir /r "$INSTDIR\packages"
  Delete "$INSTDIR\Star Racing.exe"
  Delete "$INSTDIR\Update.exe"
  Delete "$INSTDIR\.portable"
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "Не удалось удалить файлы игры. Закройте игру и повторите удаление."
    Abort
  ${EndIf}
  ReadRegStr $0 HKLM "${ARP}" "DesktopShortcut"
  ${If} $0 != ""
    Delete "$0"
  ${EndIf}
  Delete "$DESKTOP\Star Racing.lnk" ; Remove legacy common shortcut too.
  Delete "$INSTDIR\Star Racing.ico"
  Delete "$SMPROGRAMS\Star Racing.lnk"
  Delete "$INSTDIR\installer.complete"
  Delete "$INSTDIR\Uninstall-Star-Racing.exe"
  DeleteRegKey HKLM "${ARP}"
  RMDir "$INSTDIR"
SectionEnd
