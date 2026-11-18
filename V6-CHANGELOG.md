# XG Launcher Phase 13 v6 source patch

- CatalogPages.cs: stage labels now occupy collision-free lanes, truncate with tooltips on narrow layouts, and retain progress-driven positioning within their lanes.
- LauncherDialog.cs: message height estimates based on available width and caps at 420 DIP.
- DownloadSurface.cs: suppress ListBoxItem focus-within chrome in addition to existing states.

Verification: source inspection and ZIP integrity only. .NET SDK unavailable; build and Windows GUI behavior are NOT verified.
Known limitation: stage labels use per-stage lanes to avoid overlap, which limits movement to the available lane; not a pixel-perfect global-progress animation.
