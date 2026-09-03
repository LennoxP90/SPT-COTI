import * as THREE from '/coti-assets/vendor/three.module.js';

// Blender-style orientation cube. Six labelled faces; clicking one flies the camera to look
// down that axis. Drawn in its own scene over the corner of the viewport.

const FACES = [
  { label: 'X', normal: [ 1, 0, 0] },
  { label: '-X', normal: [-1, 0, 0] },
  { label: 'Y', normal: [ 0, 1, 0] },
  { label: '-Y', normal: [ 0, -1, 0] },
  { label: 'Z', normal: [ 0, 0, 1] },
  { label: '-Z', normal: [ 0, 0, -1] },
];

function faceTexture(label, accent) {
  const s = 128;
  const c = document.createElement('canvas');
  c.width = c.height = s;
  const g = c.getContext('2d');
  g.fillStyle = '#2b333f';
  g.fillRect(0, 0, s, s);
  g.strokeStyle = '#454f5e';
  g.lineWidth = 6;
  g.strokeRect(3, 3, s - 6, s - 6);
  g.fillStyle = accent;
  g.font = 'bold 54px system-ui, sans-serif';
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.fillText(label, s / 2, s / 2 + 2);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

// Axis colours match the control panel.
const ACCENT = { X: '#e5484d', Y: '#46a758', Z: '#3b82f6' };

export class ViewCube {
  constructor(hostEl, mainCamera, controls, size = 96) {
    this.main = mainCamera;
    this.controls = controls;
    this.size = size;
    this.animating = null;

    this.el = document.createElement('div');
    this.el.className = 'coti-viewcube';
    this.el.style.width = this.el.style.height = `${size}px`;
    hostEl.appendChild(this.el);

    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    this.renderer.setPixelRatio(devicePixelRatio);
    this.renderer.setSize(size, size);
    this.el.appendChild(this.renderer.domElement);

    this.scene = new THREE.Scene();
    this.camera = new THREE.OrthographicCamera(-1.6, 1.6, 1.6, -1.6, 0.1, 20);
    this.camera.position.set(0, 0, 5);

    const materials = ['X', '-X', 'Y', '-Y', 'Z', '-Z'].map(l =>
      new THREE.MeshBasicMaterial({ map: faceTexture(l, ACCENT[l.replace('-', '')]) }));
    // BoxGeometry material order is +X, -X, +Y, -Y, +Z, -Z, which is the order above.
    this.cube = new THREE.Mesh(new THREE.BoxGeometry(1.7, 1.7, 1.7), materials);
    this.scene.add(this.cube);

    const edges = new THREE.LineSegments(
      new THREE.EdgesGeometry(this.cube.geometry),
      new THREE.LineBasicMaterial({ color: 0x8b95a3 }));
    this.cube.add(edges);

    this.ray = new THREE.Raycaster();
    this.renderer.domElement.addEventListener('pointerdown', e => this.onClick(e));
    this.renderer.domElement.style.cursor = 'pointer';
  }

  onClick(event) {
    const r = this.renderer.domElement.getBoundingClientRect();
    const p = new THREE.Vector2(
      ((event.clientX - r.left) / r.width) * 2 - 1,
      -((event.clientY - r.top) / r.height) * 2 + 1);
    this.ray.setFromCamera(p, this.camera);
    const hit = this.ray.intersectObject(this.cube, false)[0];
    if (!hit) return;
    // materialIndex maps straight back to the FACES table above.
    const face = FACES[hit.face.materialIndex];
    if (face) this.faceCamera(face.normal);
  }

  // Fly the main camera onto an axis, keeping its distance from the orbit target.
  faceCamera(normal) {
    const target = this.controls.target.clone();
    const dist = this.main.position.distanceTo(target);
    const to = target.clone().addScaledVector(new THREE.Vector3(...normal), dist);
    // Looking straight down Y needs a different up vector or the view rolls arbitrarily.
    const up = Math.abs(normal[1]) > 0.9 ? new THREE.Vector3(0, 0, 1) : new THREE.Vector3(0, 1, 0);
    this.animating = { from: this.main.position.clone(), to, up, t: 0 };
  }

  update(dt) {
    const a = this.animating;
    if (a) {
      a.t = Math.min(1, a.t + dt * 3.5);
      // ease in out
      const e = a.t < 0.5 ? 2 * a.t * a.t : 1 - Math.pow(-2 * a.t + 2, 2) / 2;
      this.main.position.lerpVectors(a.from, a.to, e);
      this.main.up.copy(a.up);
      this.controls.update();
      if (a.t >= 1) this.animating = null;
    }
    // The cube takes the inverse of the view rotation.
    this.cube.quaternion.copy(this.main.quaternion).invert();
    this.renderer.render(this.scene, this.camera);
  }

  dispose() {
    this.renderer.dispose();
    this.el.remove();
  }
}
