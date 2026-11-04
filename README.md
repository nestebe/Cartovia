<div align="center">

# Cartovia

**Cartes vectorielles modernes pour les applications desktop .NET — WPF et Windows Forms**

MapLibre GL JS (open source, sans compte) ou Mapbox GL JS · WebView2 · .NET 6 / 8 / 10

![Cartovia WPF showcase](docs/screenshot.png)

</div>

Cartovia embarque un moteur de rendu de cartes vectorielles WebGL dans une WebView2 et l'expose à travers une
**API C# typée** : couches, styles, expressions, marqueurs, popups, contrôles, plugins et événements remontés en .NET.
Par défaut, il fonctionne **sans aucun compte ni clé** grâce à MapLibre GL JS et aux fonds de carte gratuits
d'OpenFreeMap ; Mapbox GL JS est disponible en option, avec la même API.

## Packages

| Package | Rôle |
|---|---|
| `Cartovia.Wpf` | Contrôle `MapView` WPF (propriétés bindables, caméra synchronisée, commandes MVVM) |
| `Cartovia.WinForms` | Contrôle `MapView` Windows Forms |
| `Cartovia.Core` | API typée indépendante de l'UI : caméra, sources, **toutes les couches et propriétés de style**, expressions, annotations, plugins, événements, GeoJSON |
| `Cartovia.WebView2` | Hôte WebView2 partagé (page embarquée, messages, navigation, permissions, capture PNG, export PDF) |
| `Cartovia.Mapbox.Services` | Extension Mapbox optionnelle : clients REST Geocoding, Search Box, Directions, Matrix, Isochrone, Optimization, Map Matching, Static Images, Tilequery |

## Démarrage rapide (WPF)

```bash
dotnet add package Cartovia.Wpf
```

```xml
<Window xmlns:carto="clr-namespace:Cartovia.Wpf;assembly=Cartovia.Wpf">
    <carto:MapView x:Name="MapView"
                   MapStyle="https://tiles.openfreemap.org/styles/liberty"
                   Center="2.35, 48.85" Zoom="12" Pitch="45" />
</Window>
```

```csharp
var map = MapView.Map;
map.Loaded += async (_, _) =>
{
    await map.AddSourceAsync(new GeoJsonSource("villes", villes) { GenerateId = true });
    await map.AddLayerAsync(new CircleLayer("villes", "villes")
    {
        CircleRadius = Expr.InterpolateZoom((5, 3), (14, 12)),
        CircleColor = Expr.Match(Expr.Get("type"), "#64748b", ("capitale", "#dc2626")),
    });
    await map.ShowPopupOnClickAsync("villes", "<b>{nom}</b>");
    await map.AddPluginAsync(new DrawPlugin());
};
map.OnLayer("villes", LayerEvent.Click, (_, e) => Debug.WriteLine(e.Feature?.GetProperty<string>("nom")));
```

Avec Mapbox : `Engine="Mapbox" AccessToken="pk.…" MapStyle="mapbox://styles/mapbox/standard"`.

## Fonctionnalités

- **Deux moteurs, une API** : MapLibre GL JS v5 (par défaut, licence BSD) et Mapbox GL JS v3 (Standard, lumières 3D,
  neige/pluie, interactions sur featuresets). Versions par défaut dans `EngineDefaults`, surchargeables.
- **Couches** : 15 types de couche et 237 propriétés paint/layout générés depuis les spécifications officielles
  (`tools/generate_style_spec.py`) ; la doc XML indique le moteur compatible, la valeur par défaut, les bornes et les unités.
- **Expressions** : helpers typés (`Interpolate`, `Match`, `Case`, `Step`, `Let`, `Format`…), les 102 opérateurs de la spec
  et les opérateurs C# (`Expr.Get("v") * 2`).
- **Sources** : GeoJSON (clustering, `updateData`), vector, raster, raster-dem, raster-array, image, vidéo, `RawSource`.
- **Caméra** : `FlyToAsync` / `EaseToAsync` / `FitBoundsAsync` qui attendent la fin de l'animation, padding, globe.
- **Événements** : plus de 40 événements typés (souris, caméra, données, erreurs), `On("nom")` pour les autres,
  événements par couche, marqueurs, popups, contrôles et plugins ; throttling des événements haute fréquence.
- **Annotations et contrôles** : marqueurs (HTML, drag, popup), popups, navigation, échelle, plein écran, géolocalisation,
  attribution, terrain, globe et `HtmlControl` (contrôle HTML dont les commandes remontent en .NET).
- **Plugins** : Draw (dessin et édition), Geocoder (Nominatim, fournisseur .NET ou Mapbox), Directions (Mapbox), Turf.js ;
  `ScriptPlugin` / `MapPlugin` pour vos propres plugins (commandes, événements, appels JS → .NET).
- **Extensions** : clusters, heatmap, bâtiments 3D, terrain 3D (AWS Terrain Tiles sans clé avec MapLibre), survol et
  sélection par feature-state, popup au clic avec template, ajout GeoJSON automatique, overlays raster WMS/XYZ.
- **Robustesse** : appels mis en file jusqu'au chargement, timeouts, exceptions typées, couches utilisateur restaurées
  après un changement de style, rechargement après crash du moteur de rendu, liens ouverts dans le navigateur,
  message clair si le runtime WebView2 manque.
- **Échappatoires** : `InvokeAsync("méthode", …)` vers n'importe quelle méthode du moteur, `EvalAsync(js)`,
  `RegisterHostCallback` (appel .NET depuis le JavaScript).
- **Export** : capture PNG/JPEG et PDF.

## Fonds de carte et services

| Usage | Sans compte | Avec Mapbox |
|---|---|---|
| Fonds de carte | OpenFreeMap (`MapStyles.OpenFreeMap*`), ou tout style MapLibre (MapTiler, Stadia, vos tuiles…) | Styles `mapbox://` dont Standard |
| Recherche de lieux | Nominatim / fournisseur .NET | Mapbox Geocoding |
| Terrain 3D | AWS Terrain Tiles | Mapbox Terrain DEM |
| Itinéraires | Votre service (ligne GeoJSON) | Plugin Directions, `Cartovia.Mapbox.Services` |

OpenFreeMap et Nominatim sont gratuits mais sans garantie de service ; pour une application à fort trafic, prévoyez un
fournisseur commercial ou vos propres tuiles.

## Hors ligne / assets locaux

Les moteurs et plugins sont chargés par défaut depuis les CDN officiels. Pour s'en passer :

```powershell
./tools/download-offline-assets.ps1 -Destination ./assets
```

```csharp
mapView.HostOptions.LocalAssetsFolder = @"C:\MonApp\assets";
mapView.Options.EngineScriptUrl = WebView2HostOptions.LocalAssetsBaseUrl + "maplibre-gl.js";
mapView.Options.EngineStylesheetUrl = WebView2HostOptions.LocalAssetsBaseUrl + "maplibre-gl.css";
```

## Points d'attention

- **Runtime WebView2** : intégré à Windows 11 et quasi universel sous Windows 10 ; sinon le contrôle affiche un lien de téléchargement.
- **WPF « airspace »** : la WebView2 classique est une fenêtre native ; `UseCompositionControl = true` permet de superposer du contenu WPF.
- **Mapbox** : token obligatoire, facturation au chargement de carte ; un token restreint par URL doit autoriser `https://cartovia.local`.
- **.NET 6** est hors support Microsoft mais reste une cible compilée ; les tests tournent sous .NET 8 et .NET 10.

## Développement

```bash
dotnet build Cartovia.sln
dotnet test Cartovia.sln
dotnet run --project samples/Cartovia.Samples.Wpf
```

Les démos utilisent MapLibre + OpenFreeMap, ou Mapbox si la variable d'environnement `MAPBOX_ACCESS_TOKEN` est définie.

Régénérer les couches pour une nouvelle version des spécifications :

```bash
python tools/generate_style_spec.py --mapbox-version 14.34.2 --maplibre-version 26.5.1
```

## Publication NuGet

- Version : `VersionPrefix` dans `Directory.Build.props` (ou `-p:Version=x.y.z`).
- Local : `dotnet pack Cartovia.sln -c Release -o artifacts`.
- CI : `.github/workflows/ci.yml` compile et teste à chaque push ; un tag `v1.2.3` crée les packages (symboles `.snupkg`,
  SourceLink) et les publie sur nuget.org via le secret `NUGET_API_KEY`.

## Licence

MIT. MapLibre GL JS est sous licence BSD-3-Clause ; Mapbox GL JS, ses plugins et les services Mapbox sont soumis aux
[conditions de Mapbox](https://www.mapbox.com/legal/tos). Cartovia n'est affilié ni à MapLibre ni à Mapbox.
