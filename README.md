# Fomoxa for Unity

`com.fomoxa.unity` is a server-authoritative networking package for Unity, built on the Fomoxa protocol and its C# runtime, Fomoxa.Net. You declare network messages as plain C# classes, the package generates their codecs, and components such as `NetworkManager`, `NetworkObject` and `NetworkBehaviour` handle connections, spawning, RPCs, state, transforms and client-side prediction.

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
https://github.com/fomoxa/com.fomoxa.unity.git
```

To work from a local copy, choose Install package from disk and select `package.json`.

Then create the settings asset with Assets > Create > Fomoxa > Settings and set its Models Folder and Generated Folder. The package generates code after every script compilation, or when you choose Tools > Fomoxa > Generate.

## A first look

A model:

```csharp
using Fomoxa;
using Fomoxa.Unity;

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

The [tutorial](Documentation~/getting-started/first-project.md) builds this into a complete game.

## Documentation

The documentation is in [`Documentation~`](Documentation~/README.md) and is published with GitBook through Git Sync; `Documentation~/gitbook-docs.yaml` describes the site.

- [Introduction](Documentation~/getting-started/introduction.md)
- [Installation](Documentation~/getting-started/installation.md)
- [Concepts](Documentation~/concepts/architecture.md)
- [Example: Red Runner offline to online](Documentation~/examples/redrunner/README.md)
- [API reference](Documentation~/reference/api.md)
- [Troubleshooting](Documentation~/getting-started/troubleshooting.md)

## Not included

The Rapier physics backend and a server that runs without Unity are not implemented in this version. The package has no lockstep mode, match recording, matchmaking, relay or encryption. The `fomoxac` code generator is not bundled for macOS, so code generation does not run in the macOS Editor.

## License

Apache-2.0. See [LICENSE.md](LICENSE.md).
