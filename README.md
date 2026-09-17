# RimBridge

Local HTTP bridge that exposes RimWorld's engine, map and player controls to an external agent. Listens on
`127.0.0.1:8765` only. Built for [rimagent](https://github.com/zorrobyte/rimagent) (an LLM agent that plays
RimWorld), but usable standalone by anything that wants programmatic control of a running game.

`POST /rpc {"method": "state.summary", "params": {}}`, plus `GET /health /methods /events?since= /screenshot?x=&z=&w=`.
Method groups: `game.* state.* map.* ui.* engine.* defs.* dev.* steward.*` — see the `[Rpc(name, doc)]` attributes
under `Source/`.

Includes an optional **Steward** layer (`Source/Steward/`): a work-priority scorer and synchronous stock-job
manager that run every tick without an LLM in the loop, so a director (human or agent) can set posture/targets
instead of micromanaging. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the vendored projects it's
built from.

## Build

Requires .NET SDK and the RimWorld 1.6 managed assemblies.

```
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec   # or wherever dotnet lives
dotnet build Source/RimBridge.csproj
```

Symlink or copy this repo into your RimWorld `Mods/` folder as `RimBridge`, enable it (and its Harmony
dependency) in the mod list, and restart the game (managed DLLs only load at startup).

## License

MIT — see [LICENSE](LICENSE). Portions vendored from other MIT-licensed projects; see
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
