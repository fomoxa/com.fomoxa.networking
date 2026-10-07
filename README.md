# Fomoxa for Unity

`com.fomoxa.networking` is a server-authoritative networking package for Unity, built on the Fomoxa protocol and its C# runtime, Fomoxa.Net. You declare network messages as plain C# classes, the package generates their codecs, and components such as `NetworkManager`, `NetworkObject` and `NetworkBehaviour` handle connections, spawning, RPCs, state, transforms and client-side prediction.

| | |
|---|---|
| Version | 0.1.0 |
| Unity | 6000.5 or later |
| Code generation | Windows x64 and Linux x64 Editors |
| License | Apache-2.0 |

## Features

- UDP, TCP and WebSocket transports, and one server listening on several of them at once.
- Host mode over an in-process connection.
- Network objects from prefabs or placed in scenes, with ownership and per-client visibility.
- RPCs from client to server and from server to clients, declared with attributes.
- Per-behaviour state with delta encoding, `NetworkTransform` with interpolation and teleport, `NetworkAnimator`.
- Network scenes, loaded for every client or for chosen clients.
- Client-side prediction with reconciliation and replay, including rollback of Unity 3D and 2D physics.
- WebGL clients through the browser's WebSocket.

## Installation

In Unity, open Window > Package Manager, click +, choose Install package from git URL and enter:

```
https://github.com/fomoxa/com.fomoxa.networking.git
```

To work from a local copy, choose Install package from disk and select `package.json`.

Then create the settings asset with Assets > Create > Fomoxa > Settings and set its Models Folder and Generated Folder. The package generates code after every script compilation, or when you choose Tools > Fomoxa > Generate.

## A first look

A model:

```csharp
using Fomoxa;
using Fomoxa.Networking;

[Network]
[Codec("net")]
[NetworkChannel("net", Channel.ReliableOrdered)]
public class PlayerStats
{
    [Network("u32")]
    [Codec("net")]
    public uint Jumps { get; set; }
}
```

A behaviour that replicates it and accepts an RPC from its owner:

```csharp
using Fomoxa.Networking;
using Fomoxa.Unity;
using Generated;
using UnityEngine;

public sealed partial class Player : NetworkBehaviour
{
    private readonly PlayerStats stats = new PlayerStats();

    protected override void OnRegisterState(NetworkState state)
    {
        state.Use(PlayerStatsNetAdapter.Instance, stats, previous => Debug.Log($"jumps: {stats.Jumps}"));
    }

    public void RequestJump() => SendServerRpc(nameof(Jump));

    [ServerRpc]
    private void Jump()
    {
        transform.position += Vector3.up;
        stats.Jumps++;
    }
}
```

Starting a server, a client or a host:

```csharp
manager.ServerManager.StartConnection(7777);
manager.ClientManager.StartConnection("127.0.0.1", 7777);
```

The [tutorial](https://github.com/fomoxa/unity-docs/blob/main/getting-started/first-project.md) builds this into a complete game.

## Documentation

The documentation is in [fomoxa/unity-docs](https://github.com/fomoxa/unity-docs) and is published with GitBook through Git Sync; `gitbook-docs.yaml` in that repository describes the site.

- [Introduction](https://github.com/fomoxa/unity-docs/blob/main/getting-started/introduction.md)
- [Installation](https://github.com/fomoxa/unity-docs/blob/main/getting-started/installation.md)
- [Concepts](https://github.com/fomoxa/unity-docs/blob/main/concepts/architecture.md)
- [Example: Red Runner offline to online](https://github.com/fomoxa/unity-docs/blob/main/examples/redrunner/README.md)
- [API reference](https://github.com/fomoxa/unity-docs/blob/main/reference/api.md)
- [Troubleshooting](https://github.com/fomoxa/unity-docs/blob/main/getting-started/troubleshooting.md)

## Not included

Physics prediction runs on Unity's PhysX through `RigidbodyPhysics`; the Rapier backend, with exact rollback and the same simulation on console servers, is the separate package [`com.fomoxa.networking.rapier`](https://github.com/fomoxa/networking-rapier). The console backend for a server that runs without Unity ships as source in `Standalone~`, which Unity ignores; a console project compiles it together with the Core. The package has no lockstep mode, match recording, matchmaking, relay or encryption. The `fomoxac` code generator is not bundled for macOS, so code generation does not run in the macOS Editor.

## License

Apache-2.0. See [LICENSE.md](LICENSE.md).
