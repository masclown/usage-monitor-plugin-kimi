' ============================================================================
' Start-UsageMonitor.vbs
' UsageMonitor Quick Launcher (VBScript edition)
'
' Purpose: double-click to start a pre-built UsageMonitor.App.exe without
'          requiring PowerShell. Use for daily launches after the first
'          build. Does NOT trigger a build. Run Start-UsageMonitor.ps1 to
'          build first.
' ============================================================================
Option Explicit

Dim fso, wshShell, scriptDir, exePath, config

Set fso = CreateObject("Scripting.FileSystemObject")
Set wshShell = CreateObject("WScript.Shell")

' Script directory == project root
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)

' Build configuration (default: Release). Edit below to switch.
config = "Release"

' Compiled binary path. Must match csproj's <TargetFramework>.
exePath = scriptDir & "\src\UsageMonitor.App\bin\" & config & "\net8.0-windows\UsageMonitor.App.exe"

' Fallback: try Debug build
If Not fso.FileExists(exePath) Then
    config = "Debug"
    exePath = scriptDir & "\src\UsageMonitor.App\bin\" & config & "\net8.0-windows\UsageMonitor.App.exe"
End If

If Not fso.FileExists(exePath) Then
    MsgBox "UsageMonitor binary not found at:" & vbCrLf & vbCrLf & _
           "  " & exePath & vbCrLf & vbCrLf & _
           "Please run Start-UsageMonitor.ps1 first to build the project.", _
           vbCritical, "UsageMonitor - Launch Failed"
    WScript.Quit 1
End If

' Set working directory to the exe's directory so that plugin loading and
' config I/O work correctly with relative paths.
wshShell.CurrentDirectory = fso.GetParentFolderName(exePath)

' 2nd arg 0 = hide window; 3rd arg False = do NOT wait for child exit.
' After double-clicking, this script returns immediately and the WPF app
' continues running as an independent process in the tray.
wshShell.Run """" & exePath & """", 0, False

Set wshShell = Nothing
Set fso = Nothing

WScript.Quit 0
