> **Historical document, not meARc.** This is the handoff note of the predecessor app, the
> *AR Architecture Guide*, as it stood on 5 August 2026 (superseded by `PROJECT.md` of 22 August,
> which is itself historical). meARc was forked from it on 26 August 2026 and renamed (package
> `com.alex.mearc`). The app name "TBD", the Google Sheet pipeline, Cesium and the `C:/Users/zalex`
> paths below do not apply to meARc. The current description is [README.md](README.md). Kept only
> for the history of the inherited AR pipeline.

# AR Geo Application — Project Context & Handoff (v3)

Supersedes v2. **v3 differs from v2 in that every status claim below was verified against
the actual files on disk**, not from memory of prior sessions. Where v2's claims and the
project disagreed, the project won and the discrepancy is recorded in §6.

Verified against the project on 2026-08-05. Unity **6000.4.10f1**.

Status key: ✅ Verified on disk · 🔧 Built, untested on device · ⚠️ Broken / needs action · 🔲 Not started

---

## 1. Who I Am / How to Address Me

My name is Alex. Address me as Alex in every response.

Working preferences:
- Strong preference for minimal raw code — Inspector-based, visual, no-code/low-code workflows.
- Extremely detailed, click-by-click, step-by-step instructions explaining every setting.
- Do not assume any component already exists in the scene unless explicitly confirmed.
- Decisive, root-cause diagnosis when troubleshooting — not branching "maybe try this" lists.
- All building data is entered personally — no team, no CMS.
- App name is **not yet decided** — refer to it as "the app" or use an explicit TBD placeholder.

---

## 2. The App — Concept & Scope

An AR mobile app in Unity. The user points their phone at a real building and sees a
geo-anchored marker overlaid on it. Tapping the marker opens a UI info panel with details
(name, year built, architect, building type, description, external links).

- **Audience:** general public + architects/architecture students; secondarily tourists, urban planners, historians.
- **Platforms:** Android (active target) and iOS. iOS support is currently **disabled** in ARCore Extensions settings.
- **Scale:** 100+ buildings at launch, city-wide. Long-term: multi-city.
- **Languages:** Bulgarian + English at launch.
- **Budget:** free/open-source preferred; ~€5–10 acceptable for a clearly justified paid tool.
- **App name:** TBD.

---

## 3. Confirmed Tech Stack (as installed)

| Tool | Version on disk | Purpose | Status |
|---|---|---|---|
| Unity | 6000.4.10f1 | Core engine | ✅ |
| URP | 17.4.0 | Render pipeline | ✅ |
| AR Foundation / ARCore | com.unity.xr.arcore 6.4.3 | Cross-platform AR | ✅ |
| ARCore Extensions | git `#arf6` branch, resolved to `d5510e57`, reports 1.54.0 | Geospatial API, VPS | ✅ but ⚠️ unpinned — see §6.9 |
| Geospatial Creator | (part of ARCore Extensions) | Visual GPS anchor placement | ✅ |
| Cesium for Unity | 1.19.0, **installed from `file:C:/Users/zalex/Desktop/com.cesium.unity-1.19.0.tgz`** | Editor-only 3D tile preview | ✅ but ⚠️ fragile — see §6.10 |
| TextMeshPro | via com.unity.ugui 2.0.0 | UI text | ✅ |
| Input System | 1.19.0 | AR Foundation internals + the scene's EventSystem | ✅ (see §6.4) |
| ScriptableObjects | — | Building data storage | ✅ |
| `Resources.LoadAll` | — | Runtime enumeration of all buildings | ✅ |
| Unity Localization | not installed | EN/BG string tables | 🔲 |
| Unity Mobile Notifications | not installed | Foreground local notifications | 🔲 |
| DOTween | not installed | UI animation | 🔲 |
| Unity Visual Scripting | 1.9.11 (installed, unused) | — | 🔲 |

**Active Input Handling = Both** (`activeInputHandler: 2`) ✅ — legacy `Input` and the Input
System package both work.

---

## 4. Key Architecture Decisions

| Decision | Reasoning |
|---|---|
| ScriptableObjects for building data | No backend, offline, matches solo/no-CMS workflow |
| `Resources.LoadAll<BuildingData>()` | Decouples the dataset from whichever anchors/regions happen to be loaded |
| `double` for Latitude/Longitude | Matches `ARGeospatialCreatorAnchor.Latitude`/`.Longitude` exactly; `float` risks coordinate drift |
| Screen Space — Overlay Canvas | Renders UI over the AR camera feed regardless of depth |
| Single `TabNavigationController` | One object owns tab switching rather than scripts scattered across prefabs |
| Tap detection via manual `Physics.Raycast` + legacy `Input` | Final decision after extensive debugging. Avoids Physics Raycaster / Graphic Raycaster for 3D hit-testing. `EventSystem.IsPointerOverGameObject()` **is** used to stop UI taps from also raycasting the world — see §6.4 for the touch caveat |
| `TappableObject` marker component (not Unity Tags) | Avoids tag mistakes; holds that instance's `BuildingData` reference |
| Cesium tileset shader kept out of runtime builds | Cesium tiles are an **Editor-only** anchor-placement aid — see §5.4 |
| Bottom-sheet info panel (not centered modal) | Keeps the AR camera feed visible around the edges |
| Fixed geographic grid for future regions | Auto-computed cells via `CesiumGeoreference` lat/long → local-metres, reusing existing geo-math |
| Notifications: foreground-only | Background geofencing needs native plugin work; foreground-only is achievable with Unity's own package |

---

## 5. History — Resolved Issues

Kept so nothing already ruled out gets re-suggested.

### 5.1 Early setup
Two failed project setups before the current stack. Resolved then: missing AR Background
Renderer Feature (yellow screen); Cesium shader variant explosion; Geospatial Mode not set
at session level; per-renderer AR Background feature requirements.

### 5.2 Tap-to-info — first build
Confirmed working in a plain non-AR scene with a test cube. Pattern:
`TappableObject` + `Physics.Raycast` from `Camera.main.ScreenPointToRay` + `InfoPanelController`.

### 5.3 Complete project reset
Rebuilt from scratch. Fresh Cesium / AR Foundation / ARCore Extensions, imported Google's
Geospatial Sample, enabled Geospatial + Geospatial Creator, added a Geospatial Creator
Origin and one Cube with `ARGeospatialCreatorAnchor`.

### 5.4 Build/runtime bug log (all resolved — re-verified on disk)

**Shader "Fatal Error — out of memory"** during Android build.
Root cause: `Cesium/CesiumDefaultTilesetShader` generating ~36 billion variants.
Fix: reassign each `Cesium3DTileset`'s Opaque Material to Cesium's prebuilt
`CesiumUnlitTilesetMaterial` (enable the **Packages visibility toggle** in the material
picker to see it), and tag the `Cesium3DTileset` GameObject **EditorOnly**.
✅ Verified: `Cesium3DTileset` in the scene is `m_TagString: EditorOnly`.
⚠️ Never tag the **AR Geospatial Creator Origin** EditorOnly — that would strip the anchors too.

**Yellow screen on device.** Root cause: URP Renderer missing the **AR Background Renderer
Feature**. ✅ Verified: both `Mobile_Renderer.asset` and `PC_Renderer.asset` now contain it.

**`CS0234` — `InputSystem` namespace not found.** Package cache corruption from Editor
crashes. Fix: delete `Library/PackageCache` (not all of `Library`) and let Unity re-resolve.

**Gradle `mainTemplate.gradle` DirectoryNotFoundException.** EDM4U couldn't copy into a
missing folder. Fix: create `Assets/Plugins/Android`, then Android Resolver → Resolve.
✅ Verified: folder exists with `mainTemplate.gradle`, `settingsTemplate.gradle`,
`gradleTemplate.properties`, `proguard-user.txt`.

**Cube at wrong location / no location permission prompt.** Root cause: the project-level
Geospatial checkbox only makes the module available; the runtime switch is an
`ARCoreExtensionsConfig` asset with **Geospatial Mode = Enabled**, linked into the scene's
ARCore Extensions GameObject.
✅ Verified: `GeospatialConfig.asset` has `GeospatialMode: 2` (Enabled), and
`ARCoreExtensionsProjectSettings.json` has `"GeospatialEnabled":true`,
`"GeospatialEditorEnabled":true`, `"AndroidAuthenticationStrategySetting":2` (API key) with
an Android Cloud Services API key present.

### 5.5 UI/UX exploration
Three home-screen concepts prototyped, then combined: onboarding → camera-as-home →
tap-marker-opens-bottom-sheet → three-tab bottom nav (Camera / Search / Settings) with a
Settings sub-flow. **Direction confirmed by Alex.**

---

## 6. ⚠️ Open Issues — Verified Against The Project

Items 1–8 are things v2 either got wrong or didn't know about.

### 6.1 The working scene lives inside a package sample folder ⚠️
All of Alex's work is in
`Assets/Samples/ARCore Extensions/1.54.0/Geospatial Sample/Scenes/GeospatialArf6.unity`,
and that is the **only** scene in Build Settings. `Assets/Scenes/SampleScene.unity` is still
the untouched default URP scene.

Re-importing the sample from Package Manager, or bumping ARCore Extensions, can overwrite
this folder and take the whole app with it. **Move the scene to `Assets/Scenes/` before
doing anything else.**

### 6.2 All three tab panels are enabled at once ⚠️ → fixed in code
In the scene, `CameraPanel`, `SearchPanel` and `SettingsPanel` all have `m_IsActive: 1`
(v2 claimed Search/Settings were disabled by default). On launch, "Search — coming soon"
and "Settings — coming soon" would both render over the camera feed.
**Fixed:** `TabNavigationController.Start()` now calls `ShowCamera()`, forcing a clean
initial state regardless of how the panels are left in the Editor.

### 6.3 The tab panels have no raycast blocker ⚠️ → Alex action required
`CameraPanel`, `SearchPanel` and `SettingsPanel` each contain exactly **one component** — a
RectTransform. With no `Image`, they are not raycast targets, so a tap on empty space in the
Search or Settings tab falls through to the world `Physics.Raycast` and can open a building
panel from behind another tab.
Partly mitigated in code (`CubeTapManager` now ignores taps while `CameraPanel` is hidden),
but SearchPanel and SettingsPanel still need a full-screen background `Image` — which they
need visually anyway. See §8 Step 3.

### 6.4 `IsPointerOverGameObject()` vs touch ⚠️ → fixed in code
The scene's EventSystem uses **`InputSystemUIInputModule`**, while `CubeTapManager` reads
taps through the legacy `Input` class. The two disagree about pointer ids for touch, so the
no-argument `IsPointerOverGameObject()` can return `false` on device — meaning a tap on the
Close button would close the panel *and* raycast the world behind it.
**Fixed:** `CubeTapManager` now reads `Input.GetTouch(0)` on device, passes the matching
pointer id, and checks both the no-argument and id forms.

### 6.5 `BuildingData` has no coordinates ⚠️ → Alex action required
`Assets/Resources/BuildingData/NewBuildingData.asset` contains
`buildingName: Test 1`, `description: This is a test`, `yearBuilt: 1979`,
`architect: Arni`, `buildingType: Block`, `externalLinkURL: kibela.one` — and
**no `latitude` or `longitude` fields at all** (i.e. 0, 0).

v2 §7.4 claimed the data layer was "verified end-to-end" printing correct coordinates for an
asset called `Building_001`. Neither the name nor the coordinates match what is on disk.
The Nearby and Search tabs depend entirely on these values.
**Tooling added:** `Tools → Building Data → Sync Coordinates From Anchors` copies each
anchor's lat/long into its linked asset automatically. See §8 Step 4.

### 6.6 The one anchor in the scene is not in Bulgaria ⚠️ → Alex action required
The Cube's `ARGeospatialCreatorAnchor` reads:
`_latitude: 41.714237207694566`, `_longitude: 91.57051819195864`, `_altitude: 2029.25`.
That is **western China (Xinjiang)** — roughly 5,600 km from Bulgaria, at 2 km altitude. It
looks like a leftover default from where the Geospatial Creator Origin was first dropped.
ARCore VPS coverage there is effectively nil, so this anchor cannot resolve.
Re-place the Geospatial Creator Origin and the anchor over the real building before any
device test is meaningful.

### 6.7 Two source files were not UTF-8 ⚠️ → fixed
`InfoPanelController.cs` and `BuildingDatabaseTest.cs` were saved in Windows-1252. The
separator characters were raw byte `0x95`, which is invalid UTF-8 — the meta line would have
rendered as `1979 � Arni � Block` on device. Both files are now valid UTF-8.

### 6.8 Player Settings are still at defaults ⚠️ → Alex action required
`applicationIdentifier` is **empty**, `companyName: DefaultCompany`, `productName: My project`.
The Android build will fall back to `com.DefaultCompany.Myproject`. The bundle id is
permanent once published to Google Play, and the ARCore API key should be restricted to it.
`AndroidMinSdkVersion: 29` ✅ (ARCore needs 24+). `AndroidTargetSdkVersion: 0` = Automatic.

### 6.9 ARCore Extensions is pinned to a git branch, not a version ⚠️
`manifest.json` requests `...arcore-unity-extensions.git#arf6`. `#arf6` is a moving branch.
`packages-lock.json` holds the current commit, so builds are reproducible today, but any
re-resolve can silently pull a different version. Consider pinning to a release tag.

### 6.10 Cesium is installed from a tarball on the Desktop ⚠️
`"com.cesium.unity": "file:C:/Users/zalex/Desktop/com.cesium.unity-1.19.0.tgz"`.
If that file is moved, renamed or deleted, the project stops resolving. Move it inside the
project (e.g. `<project>/LocalPackages/`) and update the path, or install from Cesium's
registry.

### 6.11 The ARCore API key is stored in plain text ⚠️
`ProjectSettings/ARCoreExtensionsProjectSettings.json` contains the Android Cloud Services
API key in clear text, and it ships inside the APK — that is normal and unavoidable for
ARCore. What matters is restricting it: in Google Cloud Console, restrict the key to the
**ARCore API** only and to the app's Android package name + signing SHA-1. Do this once
§6.8 sets a real bundle id.

### 6.12 Tap-to-info is still unverified on a device 🔧
Editor Play Mode is not a valid test: AR Foundation subsystems don't run without a device or
XR Simulation, and the `No active XR...Subsystem is available` warnings in that state are
expected, not a bug. All Inspector wiring is now confirmed correct on disk (§9), so the
remaining unknown is genuinely just device behaviour.

### 6.13 Localization is not reflected in the data model 🔲 → decision needed
`BuildingData` has single-language `buildingName` / `description` / `architect` /
`buildingType` fields, but BG + EN are both launch requirements. Unity Localization handles
UI chrome, not per-building content. **Decide this before entering 100 buildings** — it is a
5-minute change now and hours of re-entry later.

---

## 7. Data Layer — ✅ Implemented (⚠️ coordinates unpopulated, see §6.5)

### 7.1 `BuildingData.cs`
`Assets/Scripts/BuildingData.cs` — `ScriptableObject` with
`buildingName`, `description` (TextArea), `yearBuilt`, `architect`, `buildingType`
(all `string`), `latitude`/`longitude` (`double`), `externalLinkURL` (`string`).
`[CreateAssetMenu(menuName = "Building Data/Building Info")]`.

### 7.2 Folder structure
Assets must live under a folder literally named `Resources` for `Resources.LoadAll` to find
them. Current path: `Assets/Resources/BuildingData/NewBuildingData.asset` ✅.

### 7.3 Adding a building
1. Right-click `Assets/Resources/BuildingData` → Create → Building Data → Building Info.
2. Rename it identifiably (e.g. `Building_001`).
3. Fill in Name, Description, Year Built, Architect, Building Type, External Link URL.
4. Place the anchor in the scene, then run **Tools → Building Data → Sync Coordinates From
   Anchors** — no manual coordinate transcription.

### 7.4 `BuildingDatabaseTest.cs`
Diagnostic on the `DatabaseTester` GameObject. Logs the building count and each building's
coordinates, now explicitly printing `NO COORDINATES SET` for 0,0 entries.

---

## 8. Immediate Fix-Up Checklist (do these in order)

Code changes are already applied. These are the Editor steps only Alex can do.

Target milestone: **one real building in Yambol, tappable on a physical Android device.**
Do not install the Localization package until this passes — keep new variables out of the
first AR build.

1. **Move the scene** from `Assets/Samples/ARCore Extensions/1.54.0/Geospatial Sample/Scenes/`
   into `Assets/Scenes/`, rename to `Main.unity`, verify it is the enabled scene in Build Profiles.
2. **Delete** the `DatabaseTester` GameObject.
3. **XR Origin** → remove **AR Plane Manager**, **AR Raycast Manager**,
   **AR Streetscape Geometry Manager**. Keep XR Origin, AR Anchor Manager, AR Earth Manager.
4. **TapInputManager** → drag `Canvas → CameraPanel` into `CubeTapManager`'s **Camera Panel**
   field. Leave **Verbose Logging** ticked for the first build.
5. **SearchPanel** and **SettingsPanel** → Add Component → **Image**, opaque colour,
   **Raycast Target** ticked.
6. **Move the Geospatial Creator Origin to Yambol** (see §6.6 — it is currently in China) and
   place the Cube anchor on the Bezisten. Set its Box Collider **Size** to `5, 5, 5`.
7. **Fill in the BuildingData asset**, including the new **Building Id** (e.g. `bezisten_yambol`).
8. Run **Tools → Building Data → Sync Coordinates From Anchors**, then **Validate All Buildings**.
9. **Player Settings** → Company Name, Product Name, Android **Package Name** (permanent once published).
10. **Build And Run** to Android and work through the test list in §8.1.

### 8.1 Test list

Launch: no crash · location permission prompt · camera permission prompt · camera feed
renders (not yellow or black) · bottom nav visible.
Tabs: exactly one panel visible at a time; Search and Settings taps do not open a building panel.
AR: after 20–30 s of walking and panning, the cube appears on the Bezisten.
Tap: the cube opens the panel with the correct text · Close dismisses it and it stays closed ·
tapping empty sky does not open anything · Learn More opens the URL in a browser.

---

## 9. Scene Wiring — ✅ Verified Correct On Disk

Confirmed by reading the scene file, so none of this needs re-checking:

- `Main Camera` → `m_TagString: MainCamera` ✅ (`Camera.main` resolves)
- `Cube` → has `BoxCollider` ✅, `TappableObject` ✅ with `buildingData` → `NewBuildingData.asset` ✅, `ARGeospatialCreatorAnchor` ✅
- `TapInputManager` → `CubeTapManager.infoPanel` → the `InfoPanelController` on `InfoPanel` ✅ (survived the reparent into `CameraPanel` — v2 §6 item 4 is resolved)
- `InfoPanel` → `InfoPanelController` with `panelRoot`, `titleText`, `metaText`, `descriptionText`, `learnMoreButton` all assigned ✅
- `UIManager` → `TabNavigationController` with all three panel fields assigned ✅
- `Cesium3DTileset` → `m_TagString: EditorOnly` ✅
- Both URP renderers contain the AR Background Renderer Feature ✅

Hierarchy:
```
Canvas (Screen Space - Overlay)
 ├ CameraPanel
 │   └ InfoPanel  ├ TitleText ├ MetaText ├ DescriptionText ├ LearnMoreButton └ CloseButton
 ├ SearchPanel    └ "Search — coming soon"
 ├ SettingsPanel  └ "Settings — coming soon"
 └ BottomNav      ├ CameraTabButton ├ SearchTabButton └ SettingsTabButton
XR Origin └ Camera Offset └ Main Camera
AR Session · ARCore Extensions · AR Geospatial Creator Origin └ Cesium3DTileset
Cube · EventSystem · UIManager · TapInputManager · DatabaseTester · Directional Light
```

---

## 10. Scripts

| File | Role |
|---|---|
| `Assets/Scripts/BuildingData.cs` | ScriptableObject data model |
| `Assets/Scripts/TappableObject.cs` | Marker component holding one `BuildingData` reference |
| `Assets/Scripts/InfoPanelController.cs` | Shows/hides the info panel, fills TMP fields, opens external links |
| `Assets/Scripts/CubeTapManager.cs` | Tap → raycast → open panel |
| `Assets/Scripts/TabNavigationController.cs` | Bottom-nav tab switching |
| `Assets/Scripts/BuildingDatabaseTest.cs` | Diagnostic — logs all loaded buildings |
| `Assets/Scripts/Editor/BuildingDataTools.cs` | Editor menu: sync coordinates from anchors, validate all buildings |

---

## 11. Agreed Future Architecture

Build order: **Data Layer (✅) → Navigation Shell (🔧) → device-verify the tap flow (🔲) →
Nearby tab (🔲) → Search tab (🔲) → Settings tab (🔲) → Region Manager (🔲, deferred)**

### 11.1 Nearby tab (Camera panel)
Filter `Resources.LoadAll<BuildingData>` to buildings within **500 m** of the device
position, sorted by distance, in a scrollable list built from a reusable row prefab. Tapping
a row calls `InfoPanelController.ShowPanel()` from data — no anchor needs to be loaded.

Position source: prefer `AREarthManager.CameraGeospatialPose` when tracking is good, but fall
back to Unity's `Input.location` (GPS) so the list still works before VPS localizes. Distance
via the haversine formula.

### 11.2 Search tab
Same row prefab and list logic, fed the full unfiltered list, filtered live by a
`TMP_InputField.onValueChanged` match against `buildingName`. Must match case- and
diacritic-insensitively for Bulgarian.

### 11.3 Settings tab
- **Language:** EN/BG toggle driving Unity **Localization** (needs installing).
- **Notifications:** foreground-only via `com.unity.mobile.notifications`. Interval distance
  check against not-yet-visited buildings, with an in-session "already notified" set.
  Android 13+ requires the runtime `POST_NOTIFICATIONS` permission.
- **Replay tutorial:** re-shows the first-run onboarding panel.
- **About / Data & Sources:** static content panels.

### 11.4 Region Manager (deferred)
Reuse `CesiumGeoreference` lat/long → local-metres to compute a grid cell id per building
(local X/Z ÷ 500, floored). The same math on the user's live position gives their current
cell. `SceneManager.LoadSceneAsync`/`UnloadSceneAsync` in **Additive** mode loads the current
cell plus neighbours. Only cells containing a building need a scene file. Deliberately not
built yet — Nearby and Search are both designed to be indifferent to region splitting.

---

## 12. Open Questions

- **Final app name** — TBD.
- **Per-building localization model (§6.13)** — blocking for content entry. Options:
  (a) parallel `_en` / `_bg` fields on `BuildingData`; (b) one `BuildingData` asset per
  language; (c) Unity Localization string tables keyed by building id.
  Recommendation: **(a)** — simplest, stays Inspector-driven, one asset per building.
- **Bulk content entry** — an Editor importer that turns a spreadsheet (CSV) into
  `BuildingData` assets, and optionally auto-places anchors from those coordinates, would cut
  content entry dramatically at 100+ buildings. Not yet built; needs a go-ahead.
- **Row prefab visual design** for Nearby/Search — not yet designed.
- **Marker visual** — still a default Cube. Needs a real design, plus a distance-based scaling
  or billboard behaviour so markers stay legible at range.
- **Notification "already notified" state** — session-only, or persisted across restarts?
- **Data & Sources copy** — needs Alex's actual research workflow described accurately.
