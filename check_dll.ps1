# Check if icon resources are embedded in the DLL
$dllPath = "d:\应用开发\UsageMonitor\src\UsageMonitor.App\bin\Debug\net8.0-windows\UsageMonitor.App.dll"
$bytes = [System.IO.File]::ReadAllBytes($dllPath)
$text = [System.Text.Encoding]::ASCII.GetString($bytes)

# Search for our icon filenames in the DLL binary
$icons = @("minimax.ico", "deepseek.png", "anthropic.ico", "Providers")
foreach ($icon in $icons) {
    if ($text.Contains($icon)) {
        Write-Output "FOUND: $icon"
    } else {
        Write-Output "MISSING: $icon"
    }
}
