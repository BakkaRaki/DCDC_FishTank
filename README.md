# 🐟 Collaborative MR AquaTwin

**Collaborative MR AquaTwin** is a multiplayer mixed reality aquarium project developed using **Unity 6** and **Meta Quest 3**.

This project aims to explore the application of **Digital Twin** technology within Mixed Reality (MR), combined with **Colocation** technology, enabling multiple users within the same physical space to jointly observe, feed, and manage a virtual ecosystem.



```
![alt text](https://via.placeholder.com/1000x400?text=MR+Aquarium+Project+Screenshot)
```





## Key Features

### 1.  Behavioral Digital Twin

- **Boids Collective Intelligence**: A fish school simulation implemented using separation, alignment, and aggregation algorithms, running on the Host authority node with states synchronised in real-time to all client nodes.
- **Environmental Reverse Control**: Adjust water temperature and illumination via the "Digital Twin Console" to dynamically influence fish swimming velocity (simulating biological activity) and environmental visual effects (water colouration).

### 2. Colocation & Collaboration

- **Shared Space Anchor**: Implements a **manual calibration mechanism** that operates without requiring a cloud anchor. Users align the virtual coordinate system with a corresponding physical location (such as a table corner) through gesture-based interaction.
- **Real-time Multiplayer Interaction**: Supports Host and Client modes, with all players able to see each other's Avatars, gesture actions, and interaction outcomes.

### 3.  Immersive MR Interaction

- **Gesture Feeding**: Utilising Meta Hand Tracking, recognise the **pinch** gesture with your right hand to generate food, prompting fish to vie for it.
- **Disturbance Mechanism**: Fish shoals can detect the approach of the player's Avatar and exhibit evasive behaviour.
- **Underwater Ambience**: Panoramic translucent water rendering, suspended particles and caustic simulation, combined with Passthrough perspective backgrounds, enhance immersion.



## Tech Stack

- **Engine**: Unity 6 (LTS)
- **Platform**: Android (Meta Quest 3) / Windows (Debug Client)
- **Networking**: [Photon Fusion 2](https://www.google.com/url?sa=E&q=https%3A%2F%2Fdoc.photonengine.com%2Ffusion%2Fcurrent%2Fgetting-started%2Ffusion-intro) (Host Mode)
- **XR SDK**: Meta XR Core SDK / OpenXR
- **Architecture**: Server Authoritative (Host) + Client Prediction



## Getting Started

### Prerequisites

- Unity 6000.3.2.f1
- Meta Quest 3 Device (Enable Developer Mode)
- Photon App ID (Fusion)

### Installation 

1. Clone this repository：

   ```
   git clone https://github.com/YourUsername/MR-AquaTwin.git
   ```

2. Open the project in Unity

3. Open Tools > Fusion > Fusion Hub, and enter your **App ID** in the FusionAppSettings.

4. Open File > Build Settings and ensure the scene list contains:

   - 0: BootScene
   - 1: GameScene

### How to run

1. **Host (Quest 3)**: Connect the device and click Build and Run. Put on the headset and click **"Start Host"** on the UI.
2. **Client (PC/Quest)**:
   - **PC**(with Meta XR Simulator): In the editor, click Play, then click **"Join Client"**.
   - **Quest**: Set up another device and click **"Join Client"**。



## Controls

| Action                    | Method of operation                                          | Description                                                  |
| ------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------ |
| **Generating food**       | Right index finger and thumb **pinch together**              | Generate fish food at your fingertips to attract shoals of fish. |
| **Spatial Calibration**   | Standing at a physical anchor point (such as a table corner), **pinch together with your left hand and hold for 2 seconds**. | Align the virtual world origin to the current position to achieve colocation. |
| **Environmental Control** | Touch the **slider** on the floating panel.                  | Adjust temperature (affecting colour/speed) and light intensity. |
| **Disturbing the fish**   | Bringing one's body or hands close to a school of fish       | The shoal will scatter and flee.                             |



## Project Structure

```
Assets/
├── Scripts/
│   ├── AquariumManager.cs      # Fish School and Environment Generation Manager (Host Authority)
│   ├── SimpleBoid.cs           # The individual behaviour logic of fish (Boids algorithm)
│   ├── EnvironmentSystem.cs    # Digital Twin Environmental Data Synchronisation (Temperature/Light)
│   ├── PlayerActions.cs        # Player Gesture Interaction and Feeding (RPC)
│   ├── AvatarMovement.cs       # Player Position Synchronisation and RPC Correction
│   ├── HandColocation.cs       # Manual spatial calibration logic
│   └── DashboardUI.cs          # Collaborative Control Panel Logic
├── Prefabs/
│   ├── NetworkRunnerPrefab     # Network Core (including Spawner, Manager)
│   ├── NetworkFish             # Fish（with NetworkTransform）
│   ├── PlayerAvatar_Basic      # Player Avatar (with Hand Tracking)
│   └── NetworkEnvironment      # Environment Synchronisation Object
└── Scenes/
    ├── BootScene               # Startup and Connection Menu
    └── GameScene               # MR Main Scene
```



## Core Algorithm Specification

### Boids Fish School Simulation

Fish behaviour is implemented in SimpleBoid.cs, comprising three core vectors:

1. **Separation**: Avoid excessive crowding with neighbours.
2. **Alignment**: Follow the average direction of neighbouring elements.
3. **Cohesion (Aggregation)**: Moving towards the centre of neighbouring areas.

- *Optimisation*: Incorporated Perlin noise and dynamic patrol centres to render movement more natural.

### Digital Twin Synchronisation

Environmental data is synchronised via the [Networked] property of the EnvironmentSystem.

- **Host**: Authoritative computation of environmental status.
- Client: Monitor data changes via Render() or OnChanged to update RenderSettings (ambient light) and material colours in real time.



## License

This project is licensed under the MIT License - see the [LICENSE](https://www.google.com/url?sa=E&q=LICENSE) file for details.
