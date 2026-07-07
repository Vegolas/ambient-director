# 🎲 RPG Scene Maker

A local REST API (C# / .NET 10 Minimal API) **plus a touch control panel (Blazor WASM)** that switches your whole table mood with one tap — from a Stream Deck, an iPad, or any browser:

- **Lighting** — a Tuya smart bulb (e.g. Polux GU10) **or Philips Hue lights**, controlled **directly over your LAN** (fast, works without internet). Pick the system with `Lighting:Provider` (`tuya` / `hue`) — scenes and endpoints are identical for both.
- **Music & sound effects** — [Kenku FM](https://www.kenku.fm/) via its Remote API (playlists for ambience, soundboard for one-shot effects).
- **Scenes** — named presets in [scenes.json](src/RpgSceneMaker.Api/scenes.json) combining light color/brightness + playlist + sound effects.

Every command endpoint accepts **both GET and POST**, so the built-in Stream Deck *System → Website* action works — no plugin required.

## Running

```powershell
dotnet run --project src/RpgSceneMaker.Api
```

This serves both the API and the control panel on **http://localhost:5252** (and on your LAN — see the iPad section). The panel has four tabs: **Scenes** (one-tap presets with live active highlight), **Music** (now playing, transport, volume, playlists), **Sounds** (soundboard with playing indicators), **Lights** (mood colors, brightness, white temperature).

## Using it from an iPad (or any tablet/phone)

1. Run the API on your PC. The first `dotnet run` after this change makes Windows ask about network access — **allow it for private networks**. (If you skipped the prompt: `netsh advfirewall firewall add rule name="RPG Scene Maker" dir=in action=allow protocol=TCP localport=5252`.)
2. Find your PC's LAN address: `ipconfig` → IPv4, e.g. `192.168.1.20`. A DHCP reservation for the PC keeps the address stable.
3. On the iPad open Safari → `http://192.168.1.20:5252`, then **Share → Add to Home Screen**. It launches fullscreen like a native app.
4. Recommended: set an API key (see Security below), then enter the same key on the iPad via the ⚙ button — it is stored on the device.
5. Table tip: Settings → Display & Brightness → Auto-Lock → Never (or use Guided Access) so the panel doesn't sleep mid-session.

## One-time setup

### 1. Kenku FM

1. Install and open [Kenku FM](https://www.kenku.fm/), add your playlists (ambience) and soundboards (effects).
2. Enable the remote: **Kenku FM → Settings → Remote → Enable** (leave the default `127.0.0.1:3333`).
3. The control panel's Music/Sounds tabs list everything automatically. For `scenes.json` you need ids: `GET /music/playlists` and `GET /sfx/sounds` return them.

### 2a. Tuya bulb (local control)

*Skip this if you use Philips Hue — see 2b.*

You need three values in [appsettings.json](src/RpgSceneMaker.Api/appsettings.json): the bulb's **IP**, **device id** and **local key**.

**IP + device id** — with the API running, call:

```
http://localhost:5252/setup/scan?seconds=10
```

Tuya devices broadcast on the LAN every few seconds; the response lists `ip`, `deviceId` and the protocol version. (Windows Firewall may ask to allow the app — accept for private networks.)

**Local key** — a one-time extraction via a free Tuya developer account:

1. Sign up at [iot.tuya.com](https://iot.tuya.com) → **Cloud → Create Cloud Project** (choose the **Central Europe** data center for Poland; select the *Smart Home* / *IoT Core* API).
2. In the project: **Devices → Link App Account** → scan the QR code with your **Smart Life / Tuya Smart** app (the app your bulb is paired with). Your bulb appears in the device list.
3. Copy the project's **Access ID** and **Access Secret** (Overview tab), then call:

```
http://localhost:5252/setup/local-keys?accessId=YOUR_ACCESS_ID&apiSecret=YOUR_SECRET&deviceId=YOUR_DEVICE_ID&region=eu
```

The response contains the `localKey` for every device on your account. Put `Ip`, `DeviceId` and `LocalKey` into `appsettings.json` and restart. The cloud account is **only needed for this step** — all runtime control is local.

> **Old bulb?** If `GET /lights/status` shows data-point keys `1, 2, 3, 5` instead of `20, 21, 22, 24`, set `"Tuya:DpProfile": "v1"`. If the bulb never responds, try `"ProtocolVersion": "3.1"`.

### 2b. Philips Hue (local control)

1. Set `"Lighting": { "Provider": "hue" }` in [appsettings.json](src/RpgSceneMaker.Api/appsettings.json).
2. Find your bridge: `http://localhost:5252/setup/hue/discover` (or look up its IP in the Hue app under *Settings → My Hue system*). Put it into `Hue:BridgeIp`.
3. **Press the round link button on the Hue Bridge**, then within 30 seconds call:

   ```
   http://localhost:5252/setup/hue/register?bridgeIp=YOUR_BRIDGE_IP
   ```

   The response contains your `appKey` — put it into `Hue:AppKey`. This is one-time; all control stays on your LAN.
4. Optional: `http://localhost:5252/setup/hue/lights` lists your lights with ids. Put the ones the API should control into `Hue:LightIds` (e.g. `[ "1", "4" ]`). **Leave the list empty to control every light on the bridge.**

Tip: give the bridge a fixed IP (DHCP reservation in your router) so `Hue:BridgeIp` doesn't go stale.

### 3. Stream Deck

Use the built-in **System → Website** action (untick "Open in browser" / GET is fine), or the *Web Requests* plugin for POST:

| Button | URL |
|---|---|
| Tavern scene | `http://localhost:5252/scenes/tavern/activate` |
| Combat scene | `http://localhost:5252/scenes/combat/activate` |
| Thunder SFX | `http://localhost:5252/sfx/play?id=<soundId>` |
| Pause music | `http://localhost:5252/music/pause` |
| Light toggle | `http://localhost:5252/lights/toggle` |
| Dim to 20% | `http://localhost:5252/lights/brightness?value=20` |

## Scenes

Edit [scenes.json](src/RpgSceneMaker.Api/scenes.json) (hot-reloaded — no restart needed) or `PUT /scenes/{id}` with the same shape:

```json
{
  "id": "combat",
  "name": "⚔️ Combat",
  "light": { "power": true, "color": "#FF1E1E", "brightness": 100 },
  "music": { "playId": "<kenku playlist or track id>", "volume": 0.7 },
  "soundEffects": [ "<kenku sound id>" ]
}
```

- `light`: `color` (hex) **or** white via `brightness` + `temperature` (0 = warm, 100 = cold); `power: false` turns it off.
- `music`: `playId` starts a playlist/track, `volume` is 0–1, or `"pause": true` to stop the music.
- `soundEffects`: soundboard ids fired on activation.
- Any part can be omitted — light, music and effects are applied concurrently, and the response reports each part separately (HTTP 207 if something failed).

## Endpoint reference

| Area | Endpoints |
|---|---|
| Scenes | `GET /scenes`, `GET /scenes/active`, `GET/PUT/DELETE /scenes/{id}`, `GET\|POST /scenes/{id}/activate` |
| Lights | `/lights/on`, `/lights/off`, `/lights/toggle`, `/lights/color?hex=FF8C2A&brightness=80`, `/lights/white?brightness=80&temperature=30`, `/lights/brightness?value=50`, `GET /lights/status` |
| Music | `/music/play?id=…`, `/music/pause`, `/music/resume`, `/music/next`, `/music/previous`, `/music/volume?value=0.5`, `/music/mute`, `/music/shuffle`, `/music/repeat?mode=off\|track\|playlist`, `GET /music/playlists`, `GET /music/state` |
| SFX | `/sfx/play?id=…`, `/sfx/stop?id=…`, `GET /sfx/sounds`, `GET /sfx/state` |
| Setup (Tuya) | `GET /setup/scan?seconds=10`, `GET /setup/local-keys?accessId=…&apiSecret=…&deviceId=…&region=eu` |
| Setup (Hue) | `GET /setup/hue/discover`, `GET /setup/hue/register?bridgeIp=…`, `GET /setup/hue/lights` |

All command endpoints accept GET or POST; parameters go in the query string.

## Configuration ([appsettings.json](src/RpgSceneMaker.Api/appsettings.json))

| Key | Meaning |
|---|---|
| `Urls` | Listen address, default `http://0.0.0.0:5252` so tablets on your Wi-Fi can reach the panel. Change to `http://localhost:5252` to lock it to this PC. |
| `Security:ApiKey` | Optional shared secret. When set, all control endpoints require it (`X-Api-Key` header or `?apiKey=`); the panel asks for it under ⚙. |
| `Lighting:Provider` | `tuya` (default) or `hue` — which system scenes and `/lights` control. |
| `Tuya:Ip / DeviceId / LocalKey` | Bulb connection (see setup above). |
| `Tuya:ProtocolVersion` | `3.3` (default) or `3.1` for very old firmware. |
| `Tuya:DpProfile` | `v2` (DPs 20–24, default) or `v1` (DPs 1–5, older bulbs). |
| `Hue:BridgeIp / AppKey` | Hue Bridge connection (see setup 2b). |
| `Hue:LightIds` | Hue lights to control; empty = all lights on the bridge. |
| `Kenku:BaseUrl` | Kenku remote address, default `http://127.0.0.1:3333`. |
| `Scenes:FilePath` | Scenes file location. |

> ⚠️ The API listens on your whole LAN by default so the iPad can reach it. On a home network the worst case is someone toggling your lights, but set `Security:ApiKey` anyway — one line of config, and the panel + Stream Deck both support it. Never expose the port to the internet.
