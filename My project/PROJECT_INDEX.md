# Town Escape Horror Project - Project Index

This document provides a comprehensive index of the project structure, scripts, scenes, and assets.

## Project Information
- **Project Name**: Town Escape Horror Project
- **Type**: Unity Game Project
- **Unity Version**: See ProjectSettings/ProjectVersion.txt

---

## 📁 Project Structure

### Assets Directory

#### 🎬 Animation (`Assets/Animation/`)
- Animation files (11 .anim files)
- FBX models (12 files)
- Metadata files (27 .meta files)

#### 🎨 Art (`Assets/Art/`)
- Textures and materials (77 .png, 38 .mat files)
- Metadata files (178 .meta files)

#### 🏠 AtmosphericHouse (`Assets/AtmosphericHouse/`)
- Large asset pack with:
  - 675 prefabs
  - 656 FBX models
  - Extensive metadata (2298 .meta files)

#### 🔊 Audio (`Assets/Audio/`)
- Audio files (368 .mp3, 13 .wav files)
- Metadata files (401 .meta files)

#### 🎵 Audio Mixer (`Assets/Audio Mixer/`)
- Audio mixer configuration (1 .mixer file)

#### 📝 Editor (`Assets/Editor/`)
- Custom editor scripts (1 .cs file)

#### 🎮 Models (`Assets/Models/`)
- 3D models and textures (27 .png files, 12 .mat files)

#### 📦 Prefabs (`Assets/Prefabs/`)
- Game prefabs (62 .jpg, 48 .png files)
- Metadata files (232 .meta files)

#### 📄 Resources (`Assets/Resources/`)
- JSON configuration files (2 .json files)

#### 🎭 Scenes (`Assets/Scenes/`)
- Main Scene (Main Scene.unity)
- Main Menu (Main Menu.unity)
- Cinematics (Cinematics.unity)

#### ⚙️ Scriptable Objects (`Assets/Scriptable Objects/`)
- Scriptable object assets (4 .asset files)

#### 📜 Scripts (`Assets/Scripts/`)
See detailed script breakdown below.

#### 🎬 Sequences (`Assets/Sequences/`)
- Timeline sequences (4 .playable files, 3 .asset files)

#### ⚙️ Settings (`Assets/Settings/`)
- Configuration settings (8 .asset files)

#### 🗺️ Terrain (`Assets/Terrain/`)
- Terrain data and layers (9 .asset, 4 .terrainlayer files)

#### 📝 TextMesh Pro (`Assets/TextMesh Pro/`)
- TextMesh Pro resources (14 .shader files, 5 .asset files)

#### 🖥️ UI (`Assets/UI/`)
- UI prefabs (1 .prefab file)

#### 🎨 Unity Assets (`Assets/Unity Assets/`)
- Additional Unity assets (20 .prefab, 15 .asset files)

---

## 📜 Scripts Index

### Audio
- `Audio/FootstepController.cs` - Controls footstep audio playback

### Car System
- `Car System/CarEscapePoint.cs` - Handles car escape mechanics
- `Car System/CarPartSlot.cs` - Manages car part inventory slots

### Interaction System
- `Interaction System/IInteractable.cs` - Interface for interactable objects
- `Interaction System/InteractableItem.cs` - Base class for interactable items
- `Interaction System/PlayerInteractor.cs` - Player interaction controller
- `Interaction System/ConditionalInteractible.cs` - Conditional interaction logic

### Inventory
- `Inventory/InventoryItem.cs` - Item data structure
- `Inventory/InventoryManager.cs` - Core inventory management
- `Inventory/InventoryUI.cs` - Inventory user interface
- `Inventory/Storage Object.cs` - Storage container functionality

### Main Menu
- `Main Menu/MainMenuUI.cs` - Main menu UI controller
- `Main Menu/SettingsMenu.cs` - Settings menu controller

### Managers
- `Managers/JumpscareManager.cs` - Manages jump scares
- `Managers/PauseManager.cs` - Handles game pausing
- `Managers/SoundManager.cs` - Audio/sound management system

### NPC System
#### Core NPC System
- `NPC System/NPC System/NPCController.cs` - Main NPC controller
- `NPC System/NPC System/NPCData.cs` - NPC data structure
- `NPC System/NPC System/FSMController.cs` - Finite State Machine for NPC behavior
- `NPC System/NPC System/PerceptionController.cs` - NPC perception and awareness
- `NPC System/NPC System/InteractionController.cs` - NPC interaction handling
- `NPC System/NPC System/SuspicionSystem.cs` - Suspicion and detection system
- `NPC System/NPC System/NPCFootstepEvents.cs` - NPC footstep audio events
- `NPC System/NPC System/NPCAnchorManager.cs` - Manages NPC anchor points
- `NPC System/NPC System/RoomAnchor.cs` - Room-based anchor system
- `NPC System/NPC System/HotspotActionType.cs` - Defines hotspot action types

#### Navigation
- `NPC System/Navigator.cs` - NPC navigation system
- `NPC System/DoorController.cs` - Door interaction controller
- `NPC System/RoomGraph.cs` - Room navigation graph

#### Routine System
- `NPC System/Routine System/RoutineRegistery.cs` - Routine registration system
- `NPC System/Routine System/HouseAnchor.cs` - House-based anchor system
- `NPC System/Routine System/WeightedZonePlan.cs` - Weighted zone planning
- `NPC System/Routine System/WeightedPicker.cs` - Weighted random selection
- `NPC System/Routine System/RoutineIDAttribute.cs` - Attribute for routine IDs

#### Placeholder
- `NPC System/Placeholder/NPCZone.cs` - NPC zone placeholder

### Player
- `Player/CinemachineMouseLook.cs` - Mouse look control using Cinemachine
- `Player/PlayerHider.cs` - Player hiding mechanics
- `Player/HidingSpot.cs` - Hiding spot functionality
- `Player/PlayerFootstepRhythm.cs` - Player footstep rhythm system

---

## 🎬 Scenes

### Main Scene (`Assets/Scenes/Main Scene.unity`)
- Primary game scene
- Includes lighting data and lightmaps

### Main Menu (`Assets/Scenes/Main Menu.unity`)
- Main menu scene
- Includes lighting data and lightmaps

### Cinematics (`Assets/Scenes/Cinematics.unity`)
- Cinematic sequences
- Includes lighting data

---

## 🛠️ Project Files

### Solution File
- `My project.sln` - Visual Studio solution file

### Project Files
- `Assembly-CSharp.csproj` - C# assembly project
- `Assembly-CSharp-Editor.csproj` - Editor assembly project
- `JBooth.MicroSplat.Core.csproj` - MicroSplat core project
- `JBooth.MicroSplat.Core.Editor.csproj` - MicroSplat editor project

### Package Management
- `Packages/manifest.json` - Package manifest
- `Packages/packages-lock.json` - Package lock file

### Settings
- `ProjectSettings/` - Unity project settings (30+ configuration files)

### Library
- `Library/` - Unity's internal library cache (compiled assets, metadata, etc.)

---

## 🎮 Key Game Systems

1. **NPC AI System**
   - Finite State Machine (FSM) controller
   - Perception and suspicion system
   - Routine-based behavior
   - Room-based navigation

2. **Interaction System**
   - Interface-based interaction
   - Conditional interactions
   - Player interaction controller

3. **Inventory System**
   - Item management
   - Storage containers
   - UI integration

4. **Player Systems**
   - Hiding mechanics
   - Mouse look controls
   - Footstep system

5. **Audio System**
   - Sound manager
   - Footstep controllers
   - NPC audio events

6. **Menu Systems**
   - Main menu UI
   - Settings menu
   - Pause manager

7. **Horror Elements**
   - Jumpscare manager
   - Atmospheric assets

8. **Escape System**
   - Car escape mechanics
   - Car part collection

---

## 📊 Statistics

- **Total Scripts**: 39 C# files
- **Scenes**: 3 main scenes
- **Asset Folders**: 20+ organized folders
- **Major Systems**: 8 core game systems

---

## 🔍 Quick Reference

### Finding Scripts
- All scripts are located in `Assets/Scripts/`
- Scripts are organized by system/feature

### Finding Assets
- Art assets: `Assets/Art/`
- Audio: `Assets/Audio/`
- Prefabs: `Assets/Prefabs/`
- Models: `Assets/Models/`

### Finding Scenes
- All scenes: `Assets/Scenes/`
- Main game: `Assets/Scenes/Main Scene.unity`
- Menu: `Assets/Scenes/Main Menu.unity`

---

*Last Updated: Generated from project structure*
*Project: Town Escape Horror Project*

