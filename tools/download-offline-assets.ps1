<#
.SYNOPSIS
    Downloads the map engines and plugins used by Mapbox.Net into a local folder, for use without CDN
    (WebView2HostOptions.LocalAssetsFolder + MapOptions.EngineScriptUrl / plugin ScriptUrl properties).
.NOTES
    Mapbox GL JS and the Mapbox plugins are subject to the Mapbox Terms of Service.
#>
param(
    [Parameter(Mandatory = $true)] [string] $Destination,
    [string] $MapboxVersion = '3.34.2',
    [string] $MapLibreVersion = '5.24.0',
    [switch] $SkipMapbox,
    [switch] $SkipMapLibre,
    [switch] $SkipPlugins
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Destination | Out-Null

$files = [ordered]@{}
if (-not $SkipMapbox) {
    $files['mapbox-gl.js'] = "https://api.mapbox.com/mapbox-gl-js/v$MapboxVersion/mapbox-gl.js"
    $files['mapbox-gl.css'] = "https://api.mapbox.com/mapbox-gl-js/v$MapboxVersion/mapbox-gl.css"
}
if (-not $SkipMapLibre) {
    $files['maplibre-gl.js'] = "https://unpkg.com/maplibre-gl@$MapLibreVersion/dist/maplibre-gl.js"
    $files['maplibre-gl.css'] = "https://unpkg.com/maplibre-gl@$MapLibreVersion/dist/maplibre-gl.css"
}
if (-not $SkipPlugins) {
    $files['mapbox-gl-draw.js'] = 'https://unpkg.com/@mapbox/mapbox-gl-draw@1.5.2/dist/mapbox-gl-draw.js'
    $files['mapbox-gl-draw.css'] = 'https://unpkg.com/@mapbox/mapbox-gl-draw@1.5.2/dist/mapbox-gl-draw.css'
    $files['maplibre-gl-geocoder.js'] = 'https://unpkg.com/@maplibre/maplibre-gl-geocoder@1.9.4/dist/maplibre-gl-geocoder.js'
    $files['maplibre-gl-geocoder.css'] = 'https://unpkg.com/@maplibre/maplibre-gl-geocoder@1.9.4/dist/maplibre-gl-geocoder.css'
    $files['turf.min.js'] = 'https://unpkg.com/@turf/turf@7.4.0/turf.min.js'
    if (-not $SkipMapbox) {
        $files['mapbox-gl-geocoder.min.js'] = 'https://api.mapbox.com/mapbox-gl-js/plugins/mapbox-gl-geocoder/v5.1.3/mapbox-gl-geocoder.min.js'
        $files['mapbox-gl-geocoder.css'] = 'https://api.mapbox.com/mapbox-gl-js/plugins/mapbox-gl-geocoder/v5.1.3/mapbox-gl-geocoder.css'
        $files['mapbox-gl-directions.js'] = 'https://api.mapbox.com/mapbox-gl-js/plugins/mapbox-gl-directions/v4.3.3/mapbox-gl-directions.js'
        $files['mapbox-gl-directions.css'] = 'https://api.mapbox.com/mapbox-gl-js/plugins/mapbox-gl-directions/v4.3.3/mapbox-gl-directions.css'
    }
}

foreach ($name in $files.Keys) {
    $target = Join-Path $Destination $name
    Write-Host "Downloading $name"
    Invoke-WebRequest -Uri $files[$name] -OutFile $target -UseBasicParsing
}

Write-Host ""
Write-Host "Done. Serve the folder with WebView2HostOptions.LocalAssetsFolder and use URLs such as"
Write-Host "  https://assets.mapbox-net.local/maplibre-gl.js"
