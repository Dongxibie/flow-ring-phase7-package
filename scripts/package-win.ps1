# ============================================================================
#  Flow Ring - Windows packaging script (win-x64, self-contained)
#
#  ASCII ONLY. Windows PowerShell 5.1 decodes a .ps1 file without a BOM using
#  the system ANSI codepage, so non-ASCII literals in this file would be
#  corrupted on machines with a different codepage. The Chinese user guide
#  is therefore embedded below as base64 (UTF-8 bytes) and written out as
#  UTF-8 WITH BOM.
#
#  Output:
#    D:\FlowRing-Fix\release\FlowRing\                     (publish folder)
#    D:\FlowRing-Fix\release\FlowRing-1.0.0-win-x64.zip    (archive root = FlowRing\)
# ============================================================================

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot
$OutRoot  = 'D:\FlowRing-Fix\release'
$Publish  = Join-Path $OutRoot 'FlowRing'
$ZipPath  = Join-Path $OutRoot 'FlowRing-1.0.0-win-x64.zip'

Write-Host '== Flow Ring packaging (win-x64) =='
Write-Host "Repo root : $RepoRoot"
Write-Host "Output dir: $OutRoot"
Write-Host ''

# ---- 1. clean previous output ---------------------------------------------
if (Test-Path -LiteralPath $Publish) {
    Remove-Item -LiteralPath $Publish -Recurse -Force
}
New-Item -ItemType Directory -Path $Publish -Force | Out-Null

# ---- 2. publish the host (self-contained win-x64) -------------------------
$publishExit = 0
Push-Location $RepoRoot
try {
    dotnet publish 'src\DesktopHost\DesktopHost.csproj' -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $Publish
    $publishExit = $LASTEXITCODE
}
finally {
    Pop-Location
}
if ($publishExit -ne 0) {
    [Console]::Error.WriteLine("dotnet publish failed with exit code $publishExit")
    exit 1
}

# ---- 3. copy the frontend dist next to the exe ----------------------------
# Result: <publish>\packages\web\dist\index.html
$DistSource = Join-Path $RepoRoot 'packages\web\dist'
$DistTarget = Join-Path $Publish 'packages\web\dist'
if (-not (Test-Path -LiteralPath (Join-Path $DistSource 'index.html'))) {
    [Console]::Error.WriteLine("Frontend dist not found: $DistSource (run 'pnpm --filter web build' first)")
    exit 1
}
New-Item -ItemType Directory -Path $DistTarget -Force | Out-Null
Copy-Item -Path (Join-Path $DistSource '*') -Destination $DistTarget -Recurse -Force
if (-not (Test-Path -LiteralPath (Join-Path $DistTarget 'index.html'))) {
    [Console]::Error.WriteLine("Frontend copy failed: index.html missing under $DistTarget")
    exit 1
}

# ---- 4. write the Chinese user guide (UTF-8 with BOM) ---------------------
# Guide file name ("shi yong shuo ming .txt") built from code points below.
$GuideName = (-join ([char]0x4F7F, [char]0x7528, [char]0x8BF4, [char]0x660E)) + '.txt'
$GuideBase64 = @(
    'RmxvdyBSaW5nIOS9v+eUqOivtOaYjgo9PT09PT09PT09PT09PT09PT0KCkZsb3cgUmluZyDmmK/kuIDkuKrpvKDmoIfmiYvlir/lv6vmjbfnjq/vvJrmjInk',
    'vY/pvKDmoIfkvqfplK7vvIjmiJbplb/mjInkuK3plK4gLyDlj7PplK7vvInllKTlh7rnjq/lvaLoj5zljZXvvIwK5oqK5YWJ5qCH5ouW5ZCR5p+Q5Liq5pa5',
    '5ZCR5ZCO5p2+5byA77yM5Y2z5Y+v5omn6KGM5a+55bqU5Yqo5L2c77yM5LiN55So56a75byA5b2T5YmN56qX5Y+j5Y6754K56I+c5Y2V44CCCgrkuIDjgIHn',
    's7vnu5/opoHmsYIKICAxLiBXaW5kb3dzIDEwIC8gV2luZG93cyAxMe+8iDY0IOS9je+8ieOAggogIDIuIOmcgOimgSBNaWNyb3NvZnQgRWRnZSBXZWJWaWV3',
    'MiBSdW50aW1l77yaCiAgICAgLSBXaW5kb3dzIDExIOW3suiHquW4pu+8jOaXoOmcgOWuieijhe+8mwogICAgIC0gV2luZG93cyAxMCDoi6XlkK/liqjml7bm',
    'j5DnpLrnvLrlsJEgUnVudGltZe+8jOivt+WIsOW+rui9r+WumOe9keaQnOe0ouW5tuWuieijhQogICAgICAg4oCcRXZlcmdyZWVuIFdlYlZpZXcyIFJ1bnRp',
    'bWXigJ3vvIhkZXZlbG9wZXIubWljcm9zb2Z0LmNvbS9taWNyb3NvZnQtZWRnZS93ZWJ2aWV3Mu+8ieOAggoK5LqM44CB6L+Q6KGM5pa55byPCiAgMS4g5oqK',
    '5pW05LiqIEZsb3dSaW5nIOaWh+S7tuWkueino+WOi+WIsOS7u+aEj+S9jee9ru+8iOivt+WujOaVtOino+WOi++8jOS4jeimgeWPquaKiiBleGUg5ouW5Ye6',
    '5p2l77yJ44CCCiAgMi4g5Y+M5Ye7IEZsb3dSaW5nLkRlc2t0b3BIb3N0LmV4ZeOAggogIDMuIOS4u+eql+WPo+WHuueOsO+8jOWQjOaXtuS7u+WKoeagj+aJ',
    'mOebmOWHuueOsOWbvumSieWbvuagh++8jOihqOekuuW3suWwsee7quOAggoK5LiJ44CB5pON5L2c6K+05piOCiAgLSDllKTlh7rlnIbnjq/vvJrmjInkvY/p',
    'vKDmoIfkvqfplK7vvJvmiJbplb/mjInkuK3plK4gLyDplb/mjInlj7PplK7vvIjmjInkvY/nuqYgMC4xNSDnp5Lku6XkuIrvvInjgIIKICAtIOaLluWHuuaW',
    'ueWQkeaJp+ihjO+8muaMieS9j+S4jeadvu+8jOaKiuWFieagh+aLluWHuueOr+W/g+e6piAzMCDlg4/ntKDlubbmjIflkJHmn5DkuKrmlrnlkJHvvIznhLbl',
    'kI7mnb7lvIDljbPmiafooYzjgIIKICAtIOmpu+eVmeeCuemAie+8muW/q+mAn+eCueaMie+8iOS4jeWIsCAwLjE1IOenku+8ieWUpOWHuuWQjuWchueOr+mp',
    'u+eVme+8jOeCueWHu+aJh+WMuuaJp+ihjOOAggogIC0g5YWz6Zet5ZyG546v77ya5oyJIEVTQ++8jOaIlueCueWHu+WchueOr+WklueahOS7u+aEj+S9jee9',
    'ruOAggoK5Zub44CB5pWw5o2u5L2N572uCiAgLSDphY3nva7kuI7moaPmoYjvvJolQVBQREFUQSVcRmxvd1JpbmcKICAtIFdlYlZpZXcyIOi/kOihjOaVsOaN',
    'ru+8miVMT0NBTEFQUERBVEElXEZsb3dSaW5nXFdlYlZpZXcyCiAgLSDljbjovb3vvJrliKDpmaTmnKznqIvluo/mlofku7blpLnvvIzlubbliKDpmaTkuIrp',
    'naLkuKTkuKrnm67lvZXjgIIKCuS6lOOAgeaPkOekugogIC0g6YCA5Ye656iL5bqP77ya5Y+z6ZSu54K55Ye75omY55uY5Zu+5qCH77yM6YCJ5oup4oCc6YCA',
    '5Ye64oCd44CCCg=='
) -join ''
$GuideText = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($GuideBase64))
$Utf8WithBom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText((Join-Path $Publish $GuideName), $GuideText, $Utf8WithBom)

# ---- 5. ship the README (and the screenshots its relative links point at) --
# README.md references docs/screenshots/ring-over-app.png and
# docs/screenshots/studio.png. Both must exist in the publish folder, otherwise
# the extracted README shows broken images.
$ReadmeSource = Join-Path $RepoRoot 'README.md'
if (-not (Test-Path -LiteralPath $ReadmeSource)) {
    [Console]::Error.WriteLine("README.md not found: $ReadmeSource")
    exit 1
}
Copy-Item -LiteralPath $ReadmeSource -Destination (Join-Path $Publish 'README.md') -Force

$ShotsSource = Join-Path $RepoRoot 'docs\screenshots'
foreach ($shot in @('ring-over-app.png', 'studio.png')) {
    if (-not (Test-Path -LiteralPath (Join-Path $ShotsSource $shot))) {
        [Console]::Error.WriteLine("README screenshot missing: " + (Join-Path $ShotsSource $shot))
        exit 1
    }
}
$ShotsTarget = Join-Path $Publish 'docs\screenshots'
New-Item -ItemType Directory -Path $ShotsTarget -Force | Out-Null
Copy-Item -Path (Join-Path $ShotsSource '*.png') -Destination $ShotsTarget -Force

# ---- 6. zip it (archive root is the FlowRing\ folder) ---------------------
if (Test-Path -LiteralPath $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force
}
Compress-Archive -Path $Publish -DestinationPath $ZipPath -Force

# ---- 7. report ------------------------------------------------------------
$zipItem   = Get-Item -LiteralPath $ZipPath
$zipHash   = Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256
$fileCount = (Get-ChildItem -LiteralPath $Publish -Recurse -File).Count

Write-Host ''
Write-Host '== Done =='
Write-Host ('Zip path      : ' + $zipItem.FullName)
Write-Host ('Zip bytes     : ' + $zipItem.Length)
Write-Host ('SHA256        : ' + $zipHash.Hash)
Write-Host ('Publish files : ' + $fileCount)
exit 0
