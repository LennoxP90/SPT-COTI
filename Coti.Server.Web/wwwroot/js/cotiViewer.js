import * as THREE from '/coti-assets/vendor/three.module.js';
import { GLTFLoader } from '/coti-assets/vendor/GLTFLoader.js';
import { OrbitControls } from '/coti-assets/vendor/OrbitControls.js';
import { ViewCube } from '/coti-assets/js/viewCube.js';

// The ECOTI mount editor. Poses the device on its host as CotiMountTransform does.
//
// Unity is left handed with +Z forward; three is right handed. Positions map (x, y, -z), and a
// rotation about (x, y, z) becomes one about (-x, -y, z). Meshes are exported already converted;
// mount values and bone transforms are Unity and convert here.

const toThreeVec = v => new THREE.Vector3(v[0], v[1], -v[2]);
const toThreeQuat = q => new THREE.Quaternion(-q[0], -q[1], q[2], q[3]);

const AXIS_COLOUR = { x: 0xe5484d, y: 0x46a758, z: 0x3b82f6 };

// CotiMountTransform.Compute, in Unity terms.
function mountQuat(m) {
  const A = (deg, ax) => new THREE.Quaternion().setFromAxisAngle(ax, THREE.MathUtils.degToRad(deg || 0));
  const X = new THREE.Vector3(1, 0, 0);
  const Y = new THREE.Vector3(0, 1, 0);
  const Z = new THREE.Vector3(0, 0, 1);
  // Unity's Quaternion.Euler applies Z, then X, then Y.
  const basis = A(m.rotationY, Y).multiply(A(m.rotationX, X)).multiply(A(m.rotationZ, Z));
  return A(m.yawDegrees, Y).multiply(A(m.pitchDegrees, X)).multiply(A(m.rollDegrees, Z)).multiply(basis);
}

export class CotiViewer {
  constructor(root, hosts, onDirty, onMask) {
    this.root = root;
    this.hosts = hosts;
    this.onDirty = onDirty || (() => {});
    this.onMask = onMask || (() => {});
    // index into STEPS
    this.step = 1;
    this.mount = null;
    this.hostId = null;
    this.original = null;
    this.showAxes = true;

    root.classList.add('coti-viewer');
    this.viewport = document.createElement('div');
    this.viewport.className = 'coti-viewport';
    this.panel = document.createElement('div');
    this.panel.className = 'coti-remote';
    root.append(this.viewport, this.panel);

    this.fitToWindow();
    addEventListener('resize', () => this.fitToWindow());

    this.initScene();
    this.buildPanel();
    this.loop();
  }

  // Height of everything above the viewer, plus a margin.
  fitToWindow() {
    const chrome = Math.round(this.root.getBoundingClientRect().top + 28);
    this.root.style.setProperty('--coti-chrome', `${chrome}px`);
  }

  // scene

  initScene() {
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x10141a);

    this.camera = new THREE.PerspectiveCamera(42, 1, 0.001, 100);
    this.renderer = new THREE.WebGLRenderer({ antialias: true });
    this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.viewport.appendChild(this.renderer.domElement);

    this.scene.add(new THREE.HemisphereLight(0xd8e4f0, 0x1a2028, 2.0));
    const key = new THREE.DirectionalLight(0xffffff, 2.4); key.position.set(1, 1.8, 1.2);
    const fill = new THREE.DirectionalLight(0x9ec3ff, 0.9); fill.position.set(-1.4, 0.3, -0.9);
    const back = new THREE.DirectionalLight(0xffd9a0, 0.7); back.position.set(0.2, -0.8, -1.4);
    this.scene.add(key, fill, back);

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.08;

    this.cube = new ViewCube(this.viewport, this.camera, this.controls);

    // parts that do not move with the flip
    this.hostGroup = new THREE.Group();
    // the bone the goggles turn about
    this.pivotNode = new THREE.Group();
    // the mount transform lives here
    this.ecotiHolder = new THREE.Group();
    this.ecotiHolder.matrixAutoUpdate = false;
    this.axes = new THREE.Group();
    this.scene.add(this.hostGroup, this.pivotNode, this.axes);
    this.flip = 0;

    this.loader = new GLTFLoader();
    this.clock = new THREE.Clock();
    new ResizeObserver(() => this.resize()).observe(this.viewport);
  }

  resize() {
    const w = this.viewport.clientWidth;
    const h = this.viewport.clientHeight;
    if (!w || !h) return;
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
  }

  load(url) {
    return new Promise((ok, no) => this.loader.load(url, g => ok(g.scene), undefined, no));
  }

  paint(node, colour, metal) {
    node.traverse(o => {
      if (o.isMesh) o.material = new THREE.MeshStandardMaterial(
        { color: colour, roughness: 0.52, metalness: metal, envMapIntensity: 0.6 });
    });
    return node;
  }

  async setHost(hostId) {
    this.hostId = hostId;
    this.host = this.hosts[hostId];
    this.mount = structuredClone(this.host.mount);
    this.original = structuredClone(this.host.mount);

    this.stopAnim();
    this.hostGroup.clear();
    this.pivotNode.clear();
    this.ecotiHolder.clear();
    this.flip = 0;

    // A host whose whole body flips has no static half; there is nothing to put in hostGroup.
    if (this.host.hasStaticMesh !== false) {
      this.hostGroup.add(this.paint(await this.load(`/coti/mesh/${this.host.slug}`), 0x99a3ae, 0.15));
    }
    if (this.host.hasPivotMesh) {
      this.pivotNode.add(this.paint(await this.load(`/coti/mesh/${this.host.slug}_pivot`), 0x99a3ae, 0.15));
    }
    this.ecotiHolder.add(this.paint(await this.load('/coti/mesh/ecoti'), 0xd9772a, 0.25));
    this.pivotNode.add(this.ecotiHolder);

    this.maskPreset = this.host.maskPreset;
    this.paintMask();

    this.apply();
    this.frame();
    this.setView('game');
    this.refresh();
    this.resize();
  }

  // Marks the layout in effect and says what to compare it against.
  paintMask() {
    const masks = this.panel.querySelector('#c-mask');
    if (!masks) return;

    [...masks.children].forEach(b => b.className = b.dataset.mask === this.maskPreset ? 'on' : '');

    const note = this.panel.querySelector('#c-masknote');
    if (note) note.textContent = this.host.maskBlurb || '';
  }

  // Fits both objects to the viewport. Distance comes from the bounding sphere and the vertical
  // field of view.
  frame(wide = false) {
    // Centred on the device, not the assembly.
    const device = new THREE.Box3().setFromObject(this.ecotiHolder).getBoundingSphere(new THREE.Sphere());
    const sphere = wide
      ? new THREE.Box3().setFromObject(this.hostGroup).union(
          new THREE.Box3().setFromObject(this.pivotNode)).getBoundingSphere(new THREE.Sphere())
      // The sphere bounds a world-axis-aligned box around a rotated device, so it already
      // over-estimates the extent.
      : new THREE.Sphere(device.center, device.radius * 1.5);
    const fov = THREE.MathUtils.degToRad(this.camera.fov);
    const dist = (sphere.radius / Math.sin(fov / 2)) * 1.05;

    this.frameRadius = sphere.radius;
    this.controls.target.copy(sphere.center);
    this.camera.position.copy(sphere.center)
      .add(new THREE.Vector3(0.62, 0.34, 0.71).normalize().multiplyScalar(dist));
    this.camera.near = Math.max(sphere.radius / 500, 0.0005);
    this.camera.far = dist + sphere.radius * 8;
    this.camera.updateProjectionMatrix();
    this.controls.update();
    this.drawAxes();
  }

  // Named viewpoints. "Game" approximates the angle the item card is rendered at. Directions are
  // three-space: Unity +Z forward is -Z here, and "front" looks at the objective end.
  setView(name) {
    const dirs = {
      game: [-0.42, 0.30, -0.86],
      front: [0, 0, -1],
      back: [0, 0, 1],
      left: [1, 0, 0],
      right: [-1, 0, 0],
      top: [0, 1, 0.001],
    };
    const dir = dirs[name] || dirs.game;
    const target = this.controls.target.clone();
    const dist = this.camera.position.distanceTo(target);
    this.camera.up.set(0, 1, 0);
    this.camera.position.copy(target).add(
      new THREE.Vector3(...dir).normalize().multiplyScalar(dist));
    this.camera.lookAt(target);
    this.controls.update();
  }

  // The one place the pose is computed. bone -> mount -> world.
  apply() {
    const m = this.mount;
    const bone = this.anchorBone();

    // The pivot node carries the anchor's world transform plus the flip. The device is a child
    // of it and holds only the mount transform.
    this.pivotNode.position.copy(toThreeVec(bone.pos));
    this.pivotNode.quaternion.copy(toThreeQuat(bone.quat))
      .multiply(new THREE.Quaternion().setFromAxisAngle(
        new THREE.Vector3(1, 0, 0), THREE.MathUtils.degToRad(this.flip)));
    this.pivotNode.updateMatrixWorld(true);

    this.ecotiHolder.matrix.compose(
      toThreeVec([m.positionX || 0, m.positionY || 0, m.positionZ || 0]),
      toThreeQuat(mountQuat(m).toArray()),
      new THREE.Vector3().setScalar(m.scale > 0 ? m.scale : 1));
    this.ecotiHolder.updateMatrixWorld(true);
    this.drawAxes();
  }

  anchorBone() {
    return this.host.bones[this.mount.anchorBone]
      || this.host.bones[this.host.pivot]
      || { pos: [0, 0, 0], quat: [0, 0, 0, 1] };
  }

  // A gnomon at the device origin.
  drawAxes() {
    this.axes.clear();
    if (!this.showAxes || !this.frameRadius) return;
    const len = this.frameRadius * 0.22;
    const origin = new THREE.Vector3().setFromMatrixPosition(this.ecotiHolder.matrixWorld);
    const rot = new THREE.Quaternion().setFromRotationMatrix(this.ecotiHolder.matrixWorld);
    for (const [axis, dir] of [['x', [1, 0, 0]], ['y', [0, 1, 0]], ['z', [0, 0, 1]]]) {
      const v = new THREE.Vector3(...dir).applyQuaternion(rot);
      this.axes.add(new THREE.ArrowHelper(v, origin, len, AXIS_COLOUR[axis], len * 0.22, len * 0.13));
    }
  }

  loop() {
    this.renderer.setAnimationLoop(() => {
      const dt = this.clock.getDelta();
      this.controls.update();
      this.renderer.render(this.scene, this.camera);
      this.cube.update(dt);
    });
  }

  // the remote

  nudge(field, dir) {
    const s = STEPS[this.step];
    const by = field === 'scale' ? s.scale : (field.startsWith('position') ? s.pos : s.ang);
    const dp = field === 'scale' ? 4 : (field.startsWith('position') ? 4 : 2);
    this.mount[field] = +(((this.mount[field] || 0) + dir * by).toFixed(dp));
    this.apply(); this.refresh(); this.onDirty(this.isDirty());
  }

  set(field, value) {
    this.mount[field] = value;
    this.apply(); this.refresh(); this.onDirty(this.isDirty());
  }

  isDirty() { return JSON.stringify(this.mount) !== JSON.stringify(this.original); }

  revert() {
    this.mount = structuredClone(this.original);
    this.maskPreset = this.host.maskPreset;
    this.paintMask();
    this.apply();
    this.refresh();
    this.onDirty(false);
  }

  markSaved() {
    this.original = structuredClone(this.mount);
    // The pick is the device's own from here, so reverting later must not undo it.
    this.host.maskPreset = this.maskPreset;
    this.onDirty(false);
  }

  getMount() { return structuredClone(this.mount); }

  buildPanel() {
    this.panel.innerHTML = `
      <div class="coti-sec">
        <div class="coti-sec-h">Thermal overlay</div>
        <div class="coti-steps" id="c-mask"></div>
        <div class="coti-readout" id="c-masknote" style="text-align:left"></div>
      </div>

      <div class="coti-sec">
        <div class="coti-frame">
          <button id="c-frame">Frame device</button>
          <button id="c-frame-wide">Frame all</button>
        </div>
        <div class="coti-sec-h" style="margin-top:11px">View</div>
        <div class="coti-views">
          <button data-view="game">Game</button>
          <button data-view="front">Front</button>
          <button data-view="back">Back</button>
          <button data-view="left">Left</button>
          <button data-view="right">Right</button>
          <button data-view="top">Top</button>
        </div>
      </div>

      <div class="coti-sec" id="c-anchorsec">
        <div class="coti-sec-h">Anchor</div>
        <select id="c-anchor" class="coti-select"></select>
        <div class="coti-sec-h" style="margin-top:11px">Flip <span class="coti-unit" id="c-fliplab">0&deg;</span></div>
        <input type="range" id="c-flip" class="coti-range" min="-120" max="120" step="1" value="0">
        <div class="coti-frame" style="margin-top:7px">
          <button id="c-anim">Animate</button>
          <button id="c-flipreset">Rest</button>
        </div>
      </div>

      <div class="coti-sec">
        <div class="coti-sec-h">Step</div>
        <div class="coti-steps" id="c-steps"></div>
      </div>

      <div class="coti-sec">
        <div class="coti-sec-h">Position <span class="coti-unit">metres</span></div>
        <div class="coti-pad">
          <button class="pad up"    data-f="positionY" data-d="1"  title="up (+Y)">&#9650;</button>
          <button class="pad left"  data-f="positionX" data-d="-1" title="left (-X)">&#9664;</button>
          <div class="pad-mid" id="c-padmid">XY</div>
          <button class="pad right" data-f="positionX" data-d="1"  title="right (+X)">&#9654;</button>
          <button class="pad down"  data-f="positionY" data-d="-1" title="down (-Y)">&#9660;</button>
        </div>
        <div class="coti-depth">
          <button data-f="positionZ" data-d="-1" title="back (-Z)">&#9660; back</button>
          <button data-f="positionZ" data-d="1"  title="forward (+Z)">&#9650; fwd</button>
        </div>
        <div class="coti-readout" id="c-pos"></div>
      </div>

      <div class="coti-sec">
        <div class="coti-sec-h">Rotation <span class="coti-unit">degrees</span></div>
        <div id="c-rot"></div>
      </div>

      <div class="coti-sec">
        <div class="coti-sec-h">Scale</div>
        <div id="c-scale"></div>
      </div>

      <div class="coti-sec">
        <label class="coti-check"><input type="checkbox" id="c-axes" checked> Show axis gnomon</label>
      </div>`;

    const steps = this.panel.querySelector('#c-steps');
    STEPS.forEach((s, i) => {
      const b = document.createElement('button');
      b.textContent = s.name;
      b.className = i === this.step ? 'on' : '';
      b.onclick = () => {
        this.step = i;
        [...steps.children].forEach((c, j) => c.className = j === i ? 'on' : '');
      };
      steps.appendChild(b);
    });

    const masks = this.panel.querySelector('#c-mask');
    MASK_PRESETS.forEach(name => {
      const b = document.createElement('button');
      // The panel is narrow, so the button drops "tube" and the title carries it.
      b.textContent = name.replace(' tube', '');
      b.title = name;
      b.dataset.mask = name;
      b.onclick = () => {
        this.maskPreset = name;
        this.paintMask();
        this.onMask(name);
      };
      masks.appendChild(b);
    });

    this.panel.querySelectorAll('[data-f]').forEach(b =>
      b.onclick = () => this.nudge(b.dataset.f, +b.dataset.d));

    const rot = this.panel.querySelector('#c-rot');
    for (const [f, label, axis] of [
      ['rollDegrees', 'Roll', 'z'], ['pitchDegrees', 'Pitch', 'x'], ['yawDegrees', 'Yaw', 'y'],
      ['rotationX', 'Base X', 'x'], ['rotationY', 'Base Y', 'y'], ['rotationZ', 'Base Z', 'z']])
      rot.appendChild(this.spinner(f, label, axis));

    this.panel.querySelector('#c-scale').appendChild(this.spinner('scale', 'Uniform', null));

    // The anchor bone is part of the mount, so changing it counts as an edit.
    const anchor = this.panel.querySelector('#c-anchor');
    anchor.onchange = () => this.set('anchorBone', anchor.value);

    const flip = this.panel.querySelector('#c-flip');
    const label = this.panel.querySelector('#c-fliplab');
    const setFlip = deg => {
      this.flip = deg;
      flip.value = deg;
      label.textContent = `${Math.round(deg)}°`;
      this.apply();
    };
    flip.oninput = () => setFlip(+flip.value);
    this.panel.querySelector('#c-flipreset').onclick = () => setFlip(0);
    this.panel.querySelector('#c-anim').onclick = () => {
      if (this.anim) { cancelAnimationFrame(this.anim); this.anim = null; setFlip(0); return; }
      const t0 = performance.now();
      const tick = now => {
        // A slow sweep through the arc.
        const phase = ((now - t0) / 2600) % 1;
        setFlip(-60 * (1 - Math.cos(phase * Math.PI * 2)) / 2);
        this.anim = requestAnimationFrame(tick);
      };
      this.anim = requestAnimationFrame(tick);
    };

    const axes = this.panel.querySelector('#c-axes');
    axes.onchange = () => { this.showAxes = axes.checked; this.drawAxes(); };
    this.panel.querySelectorAll('[data-view]').forEach(b =>
      b.onclick = () => this.setView(b.dataset.view));
    this.panel.querySelector('#c-frame').onclick = () => this.frame(false);
    this.panel.querySelector('#c-frame-wide').onclick = () => this.frame(true);
  }

  spinner(field, label, axis) {
    const row = document.createElement('div');
    row.className = 'coti-row';
    row.innerHTML = `
      <span class="coti-lab ${axis ? 'ax-' + axis : ''}">${label}</span>
      <button class="mini" data-d="-1">&minus;</button>
      <span class="coti-val" data-v="${field}">0</span>
      <button class="mini" data-d="1">+</button>`;
    row.querySelectorAll('button').forEach(b => b.onclick = () => this.nudge(field, +b.dataset.d));
    return row;
  }

  stopAnim() {
    if (this.anim) {
      cancelAnimationFrame(this.anim);
      this.anim = null;
    }
  }

  // Interop calls this when the circuit goes away.
  dispose() {
    this.stopAnim();
    this.renderer.setAnimationLoop(null);
    this.cube.dispose();
    this.renderer.dispose();
    this.root.innerHTML = '';
  }

  refresh() {
    const m = this.mount;
    const anchor = this.panel.querySelector('#c-anchor');
    if (anchor && anchor.dataset.host !== this.hostId) {
      anchor.dataset.host = this.hostId;
      anchor.innerHTML = '';
      anchor.add(new Option('host root', ''));
      for (const name of Object.keys(this.host.bones)) anchor.add(new Option(name, name));
    }
    if (anchor) anchor.value = m.anchorBone || '';

    // Only a host with a separately exported moving half can flip. A saved anchorBone keeps the
    // card visible either way.
    const section = this.panel.querySelector('#c-anchorsec');
    if (section) section.hidden = !this.host.hasPivotMesh && !m.anchorBone;

    // setHost zeroes the flip; the slider is redrawn from it.
    const flip = this.panel.querySelector('#c-flip');
    const flipLabel = this.panel.querySelector('#c-fliplab');
    if (flip) flip.value = this.flip;
    if (flipLabel) flipLabel.textContent = `${Math.round(this.flip)}°`;

    const f = (v, d) => (v || 0).toFixed(d);
    this.panel.querySelector('#c-pos').textContent =
      `X ${f(m.positionX, 4)}   Y ${f(m.positionY, 4)}   Z ${f(m.positionZ, 4)}`;
    this.panel.querySelectorAll('[data-v]').forEach(el => {
      const k = el.dataset.v;
      el.textContent = f(m[k], k === 'scale' ? 3 : (k.startsWith('position') ? 4 : 1));
    });
  }
}

// Must match CotiMaskPresets, which owns the values these names stand for.
const MASK_PRESETS = ['Single tube', 'Dual tube', 'Quad tube'];

const STEPS = [
  { name: 'Fine', pos: 0.0005, ang: 0.5, scale: 0.005 },
  { name: 'Normal', pos: 0.002, ang: 2, scale: 0.02 },
  { name: 'Coarse', pos: 0.01, ang: 10, scale: 0.1 },
];
