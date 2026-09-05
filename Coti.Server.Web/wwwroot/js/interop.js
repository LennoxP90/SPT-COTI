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

export async function start(root, hostsJson, hostId, dotNetRef, version) {
  // Versioned to defeat the browser cache.
  const { CotiViewer } = await import(`/coti-assets/js/cotiViewer.js?v=${version}`);
  ensureStylesheet(version);
  dotNet = dotNetRef;
  viewer = new CotiViewer(root, JSON.parse(hostsJson),
    dirty => dotNet?.invokeMethodAsync('OnDirty', dirty),
    preset => dotNet?.invokeMethodAsync('OnMask', preset));
  await viewer.setHost(hostId);
  // Debug handle.
  window.__coti = viewer;
}

export async function setHost(hostId) {
  if (viewer) await viewer.setHost(hostId);
}

export function getMount() {
  return JSON.stringify(viewer ? viewer.getMount() : {});
}

export function revert() { viewer?.revert(); }

export function markSaved() { viewer?.markSaved(); }

export function stop() {
  viewer?.dispose?.();
  viewer = null;
  dotNet = null;
}
