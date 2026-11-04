# Mapbox.Net

Wrapper .NET complet de **Mapbox GL JS** et **MapLibre GL JS** pour les applications desktop Windows
(**WPF** et **Windows Forms**), via **WebView2**. Cibles : **.NET 6, .NET 8 et .NET 10**.

| Package | Rôle |
|---|---|
| `Mapbox.Net.Core` | API typée indépendante de l'UI : caméra, sources, **toutes les couches et propriétés de style**, expressions, marqueurs, popups, contrôles, plugins, événements, GeoJSON |
| `Mapbox.Net.WebView2` | Hôte WebView2 (page embarquée, messages, navigation, permissions, capture PNG, export PDF) |
| `Mapbox.Net.Wpf` | Contrôle `MapView` WPF (propriétés bindables, MVVM) |
| `Mapbox.Net.WinForms` | Contrôle `MapView` Windows Forms |
| `Mapbox.Net.Services` | Clients REST Mapbox : Geocoding v6, Search Box, Directions, Matrix, Isochrone, Optimization, Map Matching, Static Images, Tilequery |

## Démarrage rapide (WPF)

```bash
dotnet add package Mapbox.Net.Wpf
```

```xml
<mbx:MapView x:Name="MapView" Engine="Mapbox" AccessToken="pk.xxx"
             MapStyle="mapbox://styles/mapbox/standard" Center="2.35, 48.85" Zoom="11" />
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

Sans compte Mapbox : `Engine="MapLibre"` + `MapStyle="https://tiles.openfreemap.org/styles/liberty"` (gratuit, sans clé).

## Fonctionnalités

- **Deux moteurs** : Mapbox GL JS v3 (Standard, 3D, lumières, neige/pluie, interactions sur featuresets) et MapLibre GL JS v5
  (open source). Même API C#. Les versions par défaut sont dans `EngineDefaults` et restent surchargeables.
- **Couches** : les 15 types de couche et leurs 237 propriétés paint/layout, générés depuis les spécifications officielles
  (`tools/generate_style_spec.py`). La doc XML indique le moteur compatible, la valeur par défaut, les bornes et les unités.
  Les transitions sont typées (`CircleRadiusTransition`…).
- **Expressions** : helpers typés (`Interpolate`, `Match`, `Case`, `Step`, `Let`, `Format`…) plus les 102 opérateurs de la spec,
  et les opérateurs C# (`Expr.Get("v") * 2`).
- **Sources** : GeoJSON (clustering, `updateData`), vector, raster, raster-dem, raster-array, image, vidéo, et `RawSource`.
- **Caméra** : `FlyToAsync`/`EaseToAsync`/`FitBoundsAsync` qui attendent la fin de l'animation, padding, projection (globe…).
- **Événements** : plus de 40 événements typés (souris, caméra, données, erreurs) et `On("nom", …)` pour tous les autres.
  Événements par couche (`OnLayer`), marqueurs (clic, drag), popups, contrôles, plugins. Throttling des événements haute fréquence.
- **Annotations** : marqueurs (HTML personnalisé, drag, popup), popups, contrôles (navigation, échelle, plein écran,
  géolocalisation, attribution, terrain, globe) et `HtmlControl` (contrôle HTML avec commandes remontées en .NET).
- **Plugins** : Draw (dessin et édition), Geocoder (Mapbox, ou MapLibre avec Nominatim ou un fournisseur .NET),
  Directions (Mapbox), Turf.js ; `ScriptPlugin`/`MapPlugin` pour vos propres plugins (commandes, événements, appels JS→.NET).
- **Extensions** : clusters prêts à l'emploi, heatmap, bâtiments 3D, terrain 3D, survol et sélection par feature-state,
  popup au clic avec template, curseur au survol, ajout GeoJSON automatique, overlays raster (WMS/XYZ).
- **Robustesse** : appels mis en file jusqu'au chargement, timeouts, exceptions typées (`MapException`,
  `MapInitializationException`), couches utilisateur **restaurées après un changement de style**, rechargement
  automatique après un crash du moteur de rendu, liens ouverts dans le navigateur, message clair si le runtime WebView2 manque.
- **Échappatoires** : `InvokeAsync("n'importe quelle méthode de map", …)`, `EvalAsync(js)`, `RegisterHostCallback`
  (appel .NET depuis le JS avec `MapboxNet.callHost`).
- **Export** : capture PNG/JPEG (`SaveScreenshotAsync`) et PDF (`PrintToPdfAsync`).

## Hors ligne / assets locaux

Par défaut, les moteurs et plugins sont chargés depuis les CDN officiels. Pour un fonctionnement sans CDN :

```powershell
./tools/download-offline-assets.ps1 -Destination ./assets
```

```csharp
mapView.HostOptions.LocalAssetsFolder = @"C:\MonApp\assets";
mapView.Options.EngineScriptUrl = WebView2HostOptions.LocalAssetsBaseUrl + "maplibre-gl.js";
mapView.Options.EngineStylesheetUrl = WebView2HostOptions.LocalAssetsBaseUrl + "maplibre-gl.css";
```

Les tuiles restent servies par le fournisseur choisi : vérifiez ses conditions avant toute mise en cache.

## Points d'attention

- **Mapbox** : un token est obligatoire et la facturation se fait au « map load » (une initialisation de carte).
  Si votre token est restreint par URL, autorisez `https://mapbox-net.local`.
- **Runtime WebView2** : intégré à Windows 11 et quasi universel sous Windows 10 ; sinon le contrôle affiche un lien de téléchargement.
- **WPF « airspace »** : la WebView2 classique est une fenêtre native ; `UseCompositionControl = true` permet de superposer du contenu WPF.
- **.NET 6** est hors support Microsoft mais reste une cible compilée ; les tests tournent sous .NET 8 et .NET 10.

## Développement

```bash
dotnet build Mapbox.Net.sln
dotnet test Mapbox.Net.sln
dotnet run --project samples/Mapbox.Net.Samples.Wpf
```

Les démos utilisent Mapbox si la variable d'environnement `MAPBOX_ACCESS_TOKEN` est définie, sinon MapLibre + OpenFreeMap.

Mise à jour des couches générées (nouvelle version de la spécification) :

```bash
python tools/generate_style_spec.py --mapbox-version 14.34.2 --maplibre-version 26.5.1
```

## Publication NuGet

- Version : `VersionPrefix` dans `Directory.Build.props` (ou `-p:Version=x.y.z`).
- Local : `dotnet pack Mapbox.Net.sln -c Release -o artifacts`.
- CI : `.github/workflows/ci.yml` compile et teste à chaque push ; un tag `v1.2.3` crée les packages (avec symboles `.snupkg`
  et SourceLink) puis les publie sur nuget.org via le secret `NUGET_API_KEY`.

## Licence

MIT. Mapbox GL JS, ses plugins et les services Mapbox sont soumis aux [conditions de Mapbox](https://www.mapbox.com/legal/tos) ;
MapLibre GL JS est sous licence BSD-3-Clause. Ce projet n'est pas affilié à Mapbox.
