# Cartovia.WebView2

Microsoft Edge **WebView2** host of Cartovia, shared by **Cartovia.Wpf** and **Cartovia.WinForms**.

- `WebView2MapHost`: serves the embedded map page from `https://cartovia.local/`, relays messages, keeps navigation
  inside the page (links open in the default browser), handles permissions (geolocation opt-in), reloads the page
  after a renderer crash, captures screenshots (`CaptureAsync`) and prints to PDF (`PrintToPdfAsync`).
- `WebView2HostOptions`: user data folder (defaults to `%LOCALAPPDATA%`), fixed-version runtime, browser arguments,
  dev tools, context menu, local assets folder served at `https://assets.cartovia.local/` (offline engine copies).
- `MapViewController`: everything a control needs; use it to build a map control for another WebView2-based UI framework.
- `WebView2Environments.IsRuntimeAvailable()` / `RuntimeDownloadUrl` to check for the runtime.

**Mapbox access tokens with URL restrictions** must allow `https://cartovia.local`.
