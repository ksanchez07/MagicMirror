# AR Body Overlay — Unity + MediaPipe

A real-time augmented reality Unity application that uses MediaPipe pose detection to track a person via webcam and overlays a rigged 3D character directly onto their body. Characters and backgrounds can be swapped at runtime.

---

## Features

- Real-time pose tracking via MediaPipe Pose Landmarker
- 3D character overlaid and scaled to match the tracked person's body
- Runtime character swapping
- Runtime background swapping
- Smoothed landmark output to reduce jitter

---

## Requirements

### Unity
- Unity **2022.3 LTS** or newer
- Render Pipeline: Built-in (URP untested)

### Packages
| Package | Version | Install via |
|---|---|---|
| [MediaPipe Unity Plugin](https://github.com/homuler/MediaPipeUnityPlugin) | 0.12+ | Package Manager → Add by git URL |
| Unity Barracuda | 3.0+ | Installed automatically with MediaPipe plugin |

### Hardware
- Webcam (built-in or USB)

### Character Assets
- Humanoid-rigged FBX with **Mixamo (`mixamorig:`) bone naming** — required for Unity's Avatar auto-mapping
- Source: [Mixamo](https://mixamo.com) (free) or any compatible rigged FBX

---

## Project Structure

```
Assets/
├── MediaPipe/           # Contains MediaPipe plugin, including their sample scenes and tutorial
├── Rigged Bodies/          # FBX character models (.fbx)
├── backgrounds/         # Background textures or scene objects
├── Scripts/
│   ├── MediaPipePoseDriver.cs        # Core: drives character bones from pose landmarks
│   └── CharacterBackgroundSwapper.cs # Runtime character and background switching
├── Scenes/              # A few scenes we were testing with. Not used for the project
└── PoseDriverController.controller   # Animation Controller
```

The scene used for running our project can be found at Assets/MediaPipe/Samples/Scenes/Pose Landmark Detection/Pose Landmark Detection.unity

---

## Setup

```bash
git clone https://github.com/your-username/your-repo-name.git
```

Open the cloned folder in **Unity 2022.3 LTS or newer**, open `Assets/MediaPipe/Samples/Scenes/Pose Landmark Detection/Pose Landmark Detection.unity`, and press **Play**. The MediaPipe model files, characters, and scene wiring are all included.

> **Webcam access required.** Unity will prompt for camera permission on first run.

---

## Inspector Reference — MediaPipePoseDriver

| Field | Default | Description |
|---|---|---|
| **Runner** | — | Drag the `Solution` GameObject here |
| **Face Runner** | — | Drag the `Solution` GameObject here |
| **Cam** | Camera.main | Camera used to project landmarks into world space |
| **Character Depth** | `2` | Metres in front of camera the character sits. Increase if the character clips into the background |
| **Smoothing** | `0.85` | Landmark smoothing. Higher = smoother but more lag |
| **World Offset** | `(0,0,0)` | Fine-tune positional alignment |
| **Scale Multiplier** | `1` | Global scale adjustment on top of auto body scaling |
| **Hand IK Weight** | `1` | Wrist IK influence |
| **Foot IK Weight** | `1` | Ankle IK influence |
| **Hint IK Weight** | `0.6` | Elbow/knee hint influence (controls bend direction) |
| **Visibility Threshold** | `0.5` | Landmarks below this confidence are ignored |

---

## How It Works

### Pose-to-Character Pipeline

```
Webcam → MediaPipe PoseLandmarkerRunner
             ↓  OnPoseLandmarkerOutput event
         MediaPipePoseDriver.OnResult()
             ↓  stored thread-safely
         LateUpdate() → smoothed world positions
             ↓
         ApplyBodyScale()   — scales character to match person's shoulder span
         DriveSpine()       — rotates spine/neck/head bones directly
         OnAnimatorIK()     — drives wrists and ankles via Unity IK
```

### Coordinate System
MediaPipe outputs normalised coordinates where `(0,0)` is the top-left of the image and Y increases downward. These are converted to Unity world space using `Camera.ScreenToWorldPoint()` at `characterDepth`, which ensures the character's position and size match what the camera actually sees.

### Auto Body Scaling
On startup the script measures the character model's shoulder span in local space. Each frame it compares this to the observed world-space shoulder span from the projected landmarks and scales the character uniformly so the two match.

---

## Runtime Controls — 

Assign your character prefabs and background options in the characters array on the View Controller component. Each element will be activated when the corresponding number is pressed. By default, the assignments are:

```
0: Wrestler
1: Vampire
2: Knight
3: Demon
4: Mouse
5: Vegas
6: Guard
7: Paladin
8: Pete
9: Zombie
```

---

## Key Scripts

**`MediaPipePoseDriver.cs`** — Converts the 33 MediaPipe pose landmarks and the 468 MediaPipe face landmarks from normalised image space to Unity world space via camera projection, auto-scales the character to match the tracked person, and drives the Humanoid rig using direct bone rotation (spine, neck, head) and Unity's built-in Avatar IK (hands, feet).

---

## Credits & Licenses

- [MediaPipe Unity Plugin](https://github.com/homuler/MediaPipeUnityPlugin) — MIT License — homuler
- [MediaPipe](https://github.com/google/mediapipe) — Apache 2.0 — Google
- Character assets — see individual asset licenses at Mixamo.com
