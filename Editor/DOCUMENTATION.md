<div align="center">

# 📘 Unity MCP Vibe: Technical Reference
### Native C# Server Architecture & API Specification

[![Version](https://img.shields.io/badge/Version-3.0.0--JET-blue?style=flat-square)](releases)
[![Protocol](https://img.shields.io/badge/Protocol-MCP%202024--11--05-green?style=flat-square)](https://modelcontextprotocol.io)
[![Runtime](https://img.shields.io/badge/Runtime-Native%20C%23-000000?style=flat-square&logo=csharp)](https://docs.unity3d.com)

> **Complete technical overview of the Unity MCP Vibe architecture, including API endpoints, tool definitions, and internal file structure.**

</div>

---

## 🏗️ Architecture Overview

Unity MCP Vibe uses a **Zero-Dependency Native Architecture**. Unlike hybrid solutions that bridge Python to C#, Vibe runs a `HttpListener` directly within the Unity Editor's memory space.

### Core Systems
* **Orchestrator (`MCPNativeServer.cs`)**: Manages the lifecycle of the HTTP server and handles the main thread dispatching (Unity MainThreadDispatcher pattern).
* **Resilience (`MCPAutoCompileSystem.cs`)**: A background watcher that ensures the server automatically restarts and reconnects after a Domain Reload (script compilation) or Editor focus change.
* **Transport (`MCPHttpServer.cs`)**: Handles SSE (Server-Sent Events) for real-time communication and JSON-RPC 2.0 message parsing.

### File Structure
The codebase is organized for modularity and ease of maintenance:

```bash
Assets/Scripts/Editor/MCP/
├── Core/
│   ├── MCPNativeServer.cs       # 🧠 Main Server Logic (The Brain)
│   ├── MCPSystemWindow.cs       # 🎛️ Control Panel GUI
│   ├── MCPAutoCompileSystem.cs  # 🔄 Hot-Reload Survival Mechanism
│   └── MCPVibeSystem.cs         # 🔮 Project Vibe Analysis
├── NativeServer/
│   ├── Transport/
│   │   └── MCPHttpServer.cs     # 🌐 HTTP + SSE Implementation
│   └── Protocol/
│       └── MCPTypes.cs          # 📄 JSON-RPC 2.0 Type Definitions
└── Tools/
    ├── MCPSuperTool.cs          # ⚡ execute_csharp (God Mode)
    ├── MCPUnityPackageTool.cs   # 📦 .unitypackage Manager
    ├── MCPMasterTools.cs        # 🎬 Timeline & Director
    ├── MCPUltimateTools.cs      # 🎚️ Audio & Mixing
    └── ... (84+ Tools)
```

---

## ⚡ Super Tools

"Super Tools" are high-privilege capabilities that give the AI direct access to the C# compiler and file system.

### 1. `execute_csharp` (The Compiler Interface)
Allows the Agent to compile and run raw C# code in memory.

**Capabilities:**
* `execute`: Run void code (e.g., `Time.timeScale = 0;`)
* `evaluate`: Return a value (e.g., `return Camera.main.transform.position;`)
* `batch`: Run multiple statements.

**JSON Request Example:**
```json
{
  "name": "execute_csharp",
  "arguments": {
    "action": "execute",
    "code": "var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = \"AI_Generated\";"
  }
}
```

### 2. `unity_package` (The Asset Manager)
Deep analysis and management of `.unitypackage` files before import.

**Capabilities:**
* `analyze`: List contents without importing.
* `detect_conflicts`: Check for GUID collisions.
* `import_selective`: Import only specific files (RegEx support).

---

## 🛠️ Tool Registry

The system exposes **84+ Native Tools** categorised by domain.

### 🎮 Gameplay & Scene
| Tool | Description | Key Functions |
| :--- | :--- | :--- |
| `time_control` | Matrix manipulation | `Time.timeScale`, `fixedDeltaTime` |
| `camera_control` | Camera systems | `FOV`, `CullingMask`, `AlignToView` |
| `navmesh_control` | AI Navigation | `Bake`, `Clear`, `AgentSettings` |
| `multi_scene` | Scene Management | `Load`, `Unload`, `Merge` |

### 🔧 Editor & Workflow
| Tool | Description | Key Functions |
| :--- | :--- | :--- |
| `editor_control` | Editor State | `Refresh`, `Recompile`, `EnterPlayMode` |
| `project_settings` | Configuration | `Quality`, `Physics`, `Tags/Layers` |
| `unity_package` | Package Ops | `Export`, `Import`, `Analyze` |
| `screenshot` | Vision | `CaptureGameView`, `CaptureSceneView` |

### 🎨 Creative & Audio
| Tool | Description | Key Functions |
| :--- | :--- | :--- |
| `timeline_control` | Director API | `Play`, `Stop`, `MuteTrack` |
| `audio_mixer` | Audio System | `SetFloat`, `ClearFloat`, `Snapshots` |
| `render_settings` | Environment | `Skybox`, `Fog`, `AmbientLight` |

---

## 📡 API Specification

The server implements the **Model Context Protocol (MCP 2024-11-05)** over HTTP/SSE.

### Endpoints

| Endpoint | Method | Description | Content-Type |
| :--- | :--- | :--- | :--- |
| `/sse` | `GET` | **Handshake.** Establishes the persistent SSE connection. | `text/event-stream` |
| `/mcp` | `POST` | **Command.** Sends JSON-RPC requests to the server. | `application/json` |
| `/health`| `GET` | **Heartbeat.** Returns 200 OK if the Main Thread is responsive. | `text/plain` |

### JSON-RPC 2.0 Payload
All requests must follow this strict format:

```json
{
  "jsonrpc": "2.0",
  "id": 1234,
  "method": "tools/call",
  "params": {
    "name": "tool_name",
    "arguments": {
      "key": "value"
    }
  }
}
```

---

## ⚙️ Configuration

Configuration is persistent and managed via `EditorPrefs`. Access the GUI via **System > MCP Control Panel**.

| Variable | Key | Default | Description |
| :--- | :--- | :--- | :--- |
| **Port** | `MCP_Port` | `8080` | Port for the local HTTP server. |
| **Auto Start** | `MCP_AutoStart` | `true` | Launch server on Unity startup. |
| **Auto Restart**| `MCP_AutoRestart`| `true` | Relaunch after code recompilation. |

---

## 🚀 Installation & Setup

1.  **Deploy:** Copy the `MCP` folder to `Assets/Scripts/Editor/`.
2.  **Verify:** Check the console for `[MCP] Server started on port 8080`.
3.  **Connect:** Point your MCP Client to:
    ```
    http://localhost:8080/sse
    ```

### Requirements
* **Unity Version:** 2021.3 LTS or higher (Unity 6 Supported).
* **API Compatibility:** .NET Standard 2.1.
* **Dependencies:** `Newtonsoft.Json` (Comes with Unity Package Manager).

---

<div align="center">
  <sub><strong>Unity MCP Vibe</strong> • Native C# Architecture • 2025</sub>
</div>
