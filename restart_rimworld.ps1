# Stop RimWorld
Write-Host "Stopping RimWorld..."
Stop-Process -Name "RimWorldWin64" -Force -ErrorAction SilentlyContinue

# Resolve RimWorld path from GamePath.props
$propsPath = "Source/GamePath.props"
if (Test-Path $propsPath) {
    [xml]$xml = Get-Content $propsPath
    $rimworldPath = $xml.Project.PropertyGroup.RimWorldPath
} else {
    $rimworldPath = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld"
}

$rimworldExe = Join-Path $rimworldPath "RimWorldWin64.exe"
if (Test-Path $rimworldExe) {
    Write-Host "Relaunching RimWorld in quicktest mode..."
    Start-Process -FilePath $rimworldExe -ArgumentList "-quicktest"
} else {
    Write-Error "RimWorld executable not found at: $rimworldExe"
}
