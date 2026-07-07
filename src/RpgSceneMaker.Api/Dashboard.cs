namespace RpgSceneMaker.Api;

/// <summary>Tiny control panel served at "/": test buttons, and lists Kenku ids to copy into scenes.json.</summary>
public static class Dashboard
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>RPG Scene Maker</title>
<style>
  :root { color-scheme: dark; }
  body { font-family: system-ui, sans-serif; background: #1a1625; color: #eee8ff; margin: 0; padding: 1.5rem; }
  h1 { font-size: 1.4rem; } h2 { font-size: 1.05rem; margin: 1.6rem 0 .5rem; color: #b8a6e8; }
  .row { display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; }
  button { background: #4a3a7a; color: #fff; border: 0; border-radius: 8px; padding: .55rem .9rem; cursor: pointer; font-size: .95rem; }
  button:hover { background: #5d4a99; }
  button.scene { background: #7a3a5a; font-weight: 600; } button.scene:hover { background: #99486f; }
  input[type=range] { width: 140px; }
  #log { margin-top: 1.2rem; font-family: monospace; font-size: .8rem; color: #9a8fc0; white-space: pre-wrap; }
  ul { margin: .3rem 0; padding-left: 1.2rem; } li { margin: .15rem 0; }
  code { background: #2a2440; padding: .1rem .35rem; border-radius: 4px; cursor: pointer; }
  small { color: #9a8fc0; }
</style>
</head>
<body>
<h1>🎲 RPG Scene Maker</h1>
<small>Click a <code>code</code> id to copy it for scenes.json / Stream Deck.</small>

<h2>Scenes</h2>
<div class="row" id="scenes"><small>loading…</small></div>

<h2>Light</h2>
<div class="row">
  <button onclick="call('/lights/on')">On</button>
  <button onclick="call('/lights/off')">Off</button>
  <button onclick="call('/lights/toggle')">Toggle</button>
  <input type="color" id="color" value="#ff8c2a">
  <button onclick="call('/lights/color?hex=' + document.getElementById('color').value.slice(1))">Set color</button>
  <label>Brightness <input type="range" id="bri" min="1" max="100" value="80"
    onchange="call('/lights/brightness?value=' + this.value)"></label>
</div>

<h2>Music</h2>
<div class="row">
  <button onclick="call('/music/resume')">▶ Resume</button>
  <button onclick="call('/music/pause')">⏸ Pause</button>
  <button onclick="call('/music/previous')">⏮</button>
  <button onclick="call('/music/next')">⏭</button>
  <label>Volume <input type="range" id="vol" min="0" max="100" value="50"
    onchange="call('/music/volume?value=' + this.value / 100)"></label>
</div>

<h2>Kenku playlists</h2>
<div id="playlists"><small>Kenku FM not reachable yet.</small></div>

<h2>Kenku soundboards</h2>
<div id="soundboards"><small>Kenku FM not reachable yet.</small></div>

<div id="log"></div>

<script>
const log = (msg) => { document.getElementById('log').textContent = msg; };

async function call(url) {
  try {
    const res = await fetch(url, { method: 'POST' });
    const body = await res.text();
    log(`POST ${url} → ${res.status}\n${body}`);
  } catch (e) { log(`POST ${url} failed: ${e}`); }
}

function idItem(name, id) {
  const li = document.createElement('li');
  const codeEl = document.createElement('code');
  codeEl.textContent = id;
  codeEl.title = 'click to copy';
  codeEl.onclick = () => { navigator.clipboard.writeText(id); log(`copied ${id}`); };
  li.append(`${name} — `, codeEl);
  return li;
}

async function loadScenes() {
  const container = document.getElementById('scenes');
  try {
    const scenes = await (await fetch('/scenes')).json();
    container.innerHTML = scenes.length ? '' : '<small>No scenes yet — edit scenes.json.</small>';
    for (const scene of scenes) {
      const btn = document.createElement('button');
      btn.className = 'scene';
      btn.textContent = scene.name || scene.id;
      btn.onclick = () => call(`/scenes/${scene.id}/activate`);
      container.append(btn);
    }
  } catch (e) { container.innerHTML = '<small>failed to load scenes</small>'; }
}

async function loadKenku(url, containerId, listKey, itemKey) {
  try {
    const data = await (await fetch(url)).json();
    const container = document.getElementById(containerId);
    container.innerHTML = '';
    for (const group of data[listKey] ?? []) {
      const ul = document.createElement('ul');
      ul.append(idItem(`📁 ${group.title}`, group.id));
      const children = (data[itemKey] ?? []).filter(t => (group[itemKey] ?? []).includes(t.id));
      for (const child of children) ul.append(idItem(child.title, child.id));
      container.append(ul);
    }
  } catch (e) { /* kenku offline — leave the placeholder text */ }
}

loadScenes();
loadKenku('/music/playlists', 'playlists', 'playlists', 'tracks');
loadKenku('/sfx/sounds', 'soundboards', 'soundboards', 'sounds');
</script>
</body>
</html>
""";
}
