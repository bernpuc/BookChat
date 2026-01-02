; Installer.nsi
; Simple NSIS installer for BookChat - IRC E-Book Downloader
; Usage locally: makensis -DVERSION=1.1.0 Installer.nsi
; In CI: makensis -DVERSION=%VERSION% Installer.nsi

!define APP_NAME "BookChat"
!define EXE_NAME "BookChat.exe"
!ifndef VERSION
  !define VERSION "1.1.0"
!endif
!define INSTALLER_EXE "${APP_NAME} ${VERSION} Installer.exe"
!define PUBLISH_DIR "..\bin\Release\net9.0-windows" ; relative to the .nsi file - adjust to your dotnet publish output
!define ICON_FILE "..\Resources\favicon.ico"

; Uncomment to install per-user (no admin). Comment RequestExecutionLevel and change InstallDir accordingly.
; !define PER_USER_INSTALL

; Request elevation for per-machine install; remove or change if you use per-user install.
RequestExecutionLevel admin

; Modern UI
!include "MUI2.nsh"

; Set installer and uninstaller icons
!define MUI_ICON "${ICON_FILE}"
!define MUI_UNICON "${ICON_FILE}"

Name "${APP_NAME} ${VERSION}"
OutFile "${INSTALLER_EXE}"
InstallDir "$PROGRAMFILES\${APP_NAME}"
Icon "${ICON_FILE}"
; For per-user install, you might prefer:
; InstallDir "$LOCALAPPDATA\${APP_NAME}"

Page directory        ; allow user to select install dir
Page instfiles

Section "Install"
  SetOutPath "$INSTDIR"

  ; Copy published files - adjust PUBLISH_DIR if needed
  ; This will recursively include everything under the publish directory
  File /r "${PUBLISH_DIR}\*.*"

  ; Create Start Menu shortcut
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortCut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${EXE_NAME}" "" "$INSTDIR\${EXE_NAME}" 0

  ; Create Desktop shortcut (per-machine may require elevation)
  CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${EXE_NAME}"

  ; Write uninstall information to registry (so it appears in Programs and Features)
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "Publisher" "YourCompany"

  ; write installed path so we can detect existing installs
  WriteRegStr HKLM "Software\${APP_NAME}" "InstallPath" "$INSTDIR"

  ; CREATE THE UNINSTALLER BINARY
  WriteUninstaller "$INSTDIR\Uninstall.exe"
SectionEnd

; Create an uninstaller
Section "Uninstall"
  ; Remove shortcuts
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  Delete "$DESKTOP\${APP_NAME}.lnk"

  ; Remove files in install dir
  RMDir /r "$INSTDIR"

  ; Remove registry entries
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
  DeleteRegKey HKLM "Software\${APP_NAME}"

  ; Delete the uninstaller itself
  Delete "$INSTDIR\Uninstall.exe"
SectionEnd
