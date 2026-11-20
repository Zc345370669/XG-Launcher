Unicode true
; Keep the build-tool license scope explicit; avoid implicit compressor changes.
SetCompressor /FINAL zlib
Name "XG Launcher"
OutFile "..\dist\XGLauncher-Setup-win-x64.exe"
InstallDir "$LOCALAPPDATA\Programs\XGLauncher"
RequestExecutionLevel user
Icon "..\src\XGLauncher\Assets\XG.ico"
UninstallIcon "..\src\XGLauncher\Assets\XG.ico"

!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!include "MUI2.nsh"
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Function .onInit
  ${IfNot} ${RunningX64}
  ${OrIfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "XG Launcher requires Windows 10/11 x64."
    Abort
  ${EndIf}
FunctionEnd

Section
  SetOutPath "$INSTDIR"
  SetOverwrite on
  File "..\dist\win-x64\XG-Launcher\XGLauncher.exe"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortcut "$SMPROGRAMS\XG Launcher.lnk" "$INSTDIR\XGLauncher.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XGLauncher" "DisplayName" "XG Launcher"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XGLauncher" "DisplayVersion" "0.1.0-alpha"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XGLauncher" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XGLauncher" "InstallLocation" "$INSTDIR"
SectionEnd

Section "Uninstall"
  Delete "$SMPROGRAMS\XG Launcher.lnk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\XGLauncher"
  Delete "$INSTDIR\XGLauncher.exe"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\LICENSE.md"
  Delete "$INSTDIR\NOTICE.md"
  Delete "$INSTDIR\TRADEMARKS.md"
  Delete "$INSTDIR\PUBLICATION_STATUS.md"
  Delete "$INSTDIR\THIRD-PARTY-NOTICES.md"
  Delete "$INSTDIR\TEST-REPORT.md"
  Delete "$INSTDIR\OAUTH-FIX-REPORT.md"
  Delete "$INSTDIR\OAUTH-DEVELOPER.md"
  Delete "$INSTDIR\OAUTH-WINDOWS-VERIFICATION.md"
  Delete "$INSTDIR\BRAND-REVIEW.md"
  Delete "$INSTDIR\RELEASE-DRAFT.md"
  RMDir /r "$INSTDIR\licenses"
  RMDir /r "$INSTDIR\licensing"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
