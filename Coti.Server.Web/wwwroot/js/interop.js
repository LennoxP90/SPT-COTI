// The only surface Blazor touches. The viewer itself is free of interop.

let viewer = null;
let dotNet = null;

function ensureStylesheet(version) {
  if (document.getElementById('coti-viewer-css')) return;
  const link = document.createElement('link');
  link.id = 'coti-viewer-css';
  link.rel = 'stylesheet';
  link.href = `/coti-assets/css/coti-viewer.css?v=${version}`;
  document.head.appendChild(link);
}

export async function start(viewport, panel, hostsJson, hostId, dotNetRef, version) {
  // Versioned to defeat the browser cache.
  const { CotiViewer } = await import(`/coti-assets/js/cotiViewer.js?v=${version}`);
  ensureStylesheet(version);
  dotNet = dotNetRef;
  viewer = new CotiViewer(viewport, panel, JSON.parse(hostsJson), {
    onDirty: dirty => dotNet?.invokeMethodAsync('OnDirty', dirty),
    onSelect: selected => dotNet?.invokeMethodAsync('OnSelect', selected),
    mirror: (mountJson, anchor) => dotNet.invokeMethodAsync('Mirror', mountJson, anchor),
  });
  await viewer.setHost(hostId);
  // Debug handle.
  window.__coti = viewer;
}

export async function setHost(hostId) {
  if (viewer) await viewer.setHost(hostId);
}

// The Type selector changed the layout in use; the device is not saved yet.
export function setLayout(viewJson) { viewer?.setLayout(JSON.parse(viewJson)); }

// After a save or a revert the table is sent again, so every variant of the device matches the server.
export function refreshHosts(hostsJson, hostId) { viewer?.refreshHosts(JSON.parse(hostsJson), hostId); }

export function getMounts() {
  return JSON.stringify(viewer ? viewer.getMounts() : {});
}

export function getTexts() {
  return JSON.stringify(viewer ? viewer.getTexts() : {});
}

export function stop() {
  viewer?.dispose?.();
  viewer = null;
  dotNet = null;
}
