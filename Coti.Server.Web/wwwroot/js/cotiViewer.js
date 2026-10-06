import * as THREE from '/coti-assets/vendor/three.module.js';
import { GLTFLoader } from '/coti-assets/vendor/GLTFLoader.js';
import { OrbitControls } from '/coti-assets/vendor/OrbitControls.js';
import { ViewCube } from '/coti-assets/js/viewCube.js';

// The ECOTI mount editor. Poses the device on its host as CotiMountTransform does, and places each tube's display
// text as CotiDisplayLayout does.
//
// Unity is left handed with +Z forward; three is right handed. Positions map (x, y, -z), and a
// rotation about (x, y, z) becomes one about (-x, -y, z). Meshes are exported already converted;
// mount values and bone transforms are Unity and convert here.

const toThreeVec = v => new THREE.Vector3(v[0], v[1], -v[2]);
const toThreeQuat = q => new THREE.Quaternion(-q[0], -q[1], q[2], q[3]);

const AXIS_COLOUR = { x: 0xe5484d, y: 0x46a758, z: 0x3b82f6 };
// The selected COTI, and every other drawn one.
const COTI_ON = 0xd9772a;
const COTI_OFF = 0x4f86c6;

// A pointer that moves less than this between press and release is a click, not an orbit or a drag.
const CLICK_PX = 5;

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

// CotiDisplayLayout.TryRect, in the same steps: screen heights from the left edge, back to viewport units (x across,
// y up) at the end. `rule` carries its constants and the image size.
function textRect(c, p, aspect, rule) {
  const texel = rule.texel * c.r;
  const w = rule.width * texel;
  const h = rule.height * texel;
  const cx = c.u * aspect;
  let left = p.align === 'left' ? cx - p.edge * c.r
    : p.align === 'right' ? cx + p.edge * c.r - w
    : cx + p.edge * c.r - w / 2;
  left = Math.min(Math.max(left, rule.margin), aspect - rule.margin - w);
  const bottom = Math.min(Math.max(c.v - h / 2 + p.y * c.r, rule.margin), 1 - rule.margin - h);
  return { x: left / aspect, y: bottom, w: w / aspect, h };
}

// The placement a rect sits at: the inverse of textRect, so a value dragged past the screen's margin reads as the
// margin.
function placementOf(rect, c, align, aspect) {
  const left = rect.x * aspect;
  const w = rect.w * aspect;
  const cx = c.u * aspect;
  const edge = align === 'left' ? cx - left : align === 'right' ? left + w - cx : left + w / 2 - cx;
  return { align, edge: edge / c.r, y: (rect.y + rect.h / 2 - c.v) / c.r };
}

// Text values carry two decimals, the step the inspector and the drag use.
const snap = v => Math.round(v * 100) / 100 + 0;

export class CotiViewer {
  // viewport: the element the 3D view fills; panel: the inspector's element.
  constructor(viewport, panel, hosts, callbacks) {
    this.viewport = viewport;
    this.panel = panel;
    this.hosts = hosts;
    this.onDirty = callbacks.onDirty || (() => {});
    this.onSelect = callbacks.onSelect || (() => {});
    // (source mount JSON, target anchor) -> the mirrored mount JSON, from CotiTubeMirror on the server
    this.mirror = callbacks.mirror;
    // index into STEPS
    this.step = 1;
    // the mount on screen, which is mounts[tube] once that tube is posed
    this.mount = null;
    // label -> mount for every posed tube; a v1 device is one tube under ''
    this.mounts = {};
    // label -> text block ({ align, edge, y }, each optional) or null; a field left out keeps the rule
    this.texts = {};
    // The tube the inspector shows, and what of it is selected: 'coti', 'text' or null for the device.
    this.tube = '';
    this.sel = null;
    this.hostId = null;
    this.original = null;
    this.originalTexts = null;
    this.showAxes = true;
    // index into the host's preview screens
    this.aspect = 0;
    // label -> false for a tube whose COTI is hidden. A view setting only: never saved.
    this.visible = {};

    this.viewport.classList.add('coti-viewport');

    // A game-sized screen over the corner of the 3D view. Click a text or a circle in it to select it.
    this.screenWrap = document.createElement('div');
    this.screenWrap.className = 'coti-screen';
    this.screen = document.createElement('canvas');
    this.screen.title = 'Every circle on a game screen. Click a text to select it, drag it to move it.';
    this.screenSizes = document.createElement('div');
    this.screenSizes.className = 'coti-steps';
    this.screenWrap.append(this.screen, this.screenSizes);
    this.viewport.appendChild(this.screenWrap);
    this.initScreenInput();

    // Which tubes' COTIs are drawn, over the top right corner. Selection picks what is edited, these what is drawn.
    this.visPanel = document.createElement('div');
    this.visPanel.className = 'coti-tubevis';
    this.viewport.appendChild(this.visPanel);

    this.fitToWindow();
    this.onResize = () => this.fitToWindow();
    addEventListener('resize', this.onResize);
    // Arrow keys nudge the selected text, unless a field has the keyboard.
    this.onKey = e => this.nudgeText(e);
    addEventListener('keydown', this.onKey);

    this.initScene();
    this.buildPanel();
    this.loop();
  }

  // Height of everything above the viewer, plus a margin.
  fitToWindow() {
    const root = this.viewport.parentElement;
    const chrome = Math.round(root.getBoundingClientRect().top + 28);
    root.style.setProperty('--coti-chrome', `${chrome}px`);
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
    // the bone the goggles turn about, carrying the moving mesh
    this.pivotNode = new THREE.Group();
    this.pivotNode.matrixAutoUpdate = false;
    // one COTI per tube of the layout, label -> node holding its world pose
    this.cotis = new Map();
    this.cotiGroup = new THREE.Group();
    this.axes = new THREE.Group();
    this.scene.add(this.hostGroup, this.pivotNode, this.cotiGroup, this.axes);
    this.flip = 0;

    // A click on a COTI selects it; a click anywhere else in the 3D view selects the device.
    const canvas = this.renderer.domElement;
    canvas.addEventListener('pointerdown', e => { this.pressed = [e.clientX, e.clientY]; });
    canvas.addEventListener('pointerup', e => {
      const [x, y] = this.pressed || [NaN, NaN];
      this.pressed = null;
      if (e.button === 0 && Math.hypot(e.clientX - x, e.clientY - y) < CLICK_PX) this.pick(e);
    });

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

  // The first thing under the pointer decides: a COTI is selected, the host or nothing clears the selection.
  pick(e) {
    const box = this.renderer.domElement.getBoundingClientRect();
    const ray = new THREE.Raycaster();
    ray.setFromCamera(new THREE.Vector2(
      ((e.clientX - box.left) / box.width) * 2 - 1, -((e.clientY - box.top) / box.height) * 2 + 1), this.camera);
    const shown = o => !o || (o.visible && shown(o.parent));
    const hit = ray.intersectObjects([this.hostGroup, this.pivotNode, this.cotiGroup], true)
      .find(h => h.object.isMesh && shown(h.object));
    let label = null;
    for (let o = hit?.object; o && label === null; o = o.parent) {
      for (const [l, node] of this.cotis) if (node === o) label = l;
    }
    this.select(label === null ? null : 'coti', label ?? this.tube);
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

  // A COTI clone shares the mesh's geometry, so only the materials paint() gave it are its own.
  removeCoti(label) {
    const node = this.cotis.get(label);
    node.traverse(o => { if (o.isMesh) o.material.dispose(); });
    this.cotiGroup.remove(node);
    this.cotis.delete(label);
  }

  async setHost(hostId) {
    this.hostId = hostId;
    this.host = this.hosts[hostId];
    this.mounts = structuredClone(this.host.tubes);
    this.original = structuredClone(this.host.tubes);
    this.texts = structuredClone(this.host.texts);
    this.originalTexts = structuredClone(this.host.texts);
    this.visible = {};
    this.sel = null;
    this.tube = this.host.home;

    this.stopAnim();
    this.hostGroup.clear();
    this.pivotNode.clear();
    for (const label of [...this.cotis.keys()]) this.removeCoti(label);
    this.flip = 0;

    // A host whose whole body flips has no static half; there is nothing to put in hostGroup.
    if (this.host.hasStaticMesh !== false) {
      this.hostGroup.add(this.paint(await this.load(`/coti/mesh/${this.host.slug}`), 0x99a3ae, 0.15));
    }
    if (this.host.hasPivotMesh) {
      this.pivotNode.add(this.paint(await this.load(`/coti/mesh/${this.host.slug}_pivot`), 0x99a3ae, 0.15));
    }
    this.ecoti ??= await this.load('/coti/mesh/ecoti');

    this.showTubes();
    this.loadScreen();

    this.frame();
    this.setView('game');
    this.refresh();
    this.resize();
    this.onSelect(false);
  }

  // Every tube of the layout in use, which is the JSON's or the Type selector's: a v1 device is one tube under ''.
  labels() {
    return this.host.tabs.length ? this.host.tabs.map(t => t.label) : [''];
  }

  // One COTI node per tube, the checkboxes, then the selection, kept while its tube is still in the layout.
  showTubes() {
    const labels = this.labels();
    for (const label of [...this.cotis.keys()]) {
      if (!labels.includes(label)) this.removeCoti(label);
    }
    for (const label of labels) {
      if (this.cotis.has(label)) continue;
      const node = this.paint(this.ecoti.clone(), COTI_OFF, 0.25);
      node.matrixAutoUpdate = false;
      this.cotiGroup.add(node);
      this.cotis.set(label, node);
    }

    this.visPanel.innerHTML = '<div class="coti-sec-h">Show</div>';
    const names = this.host.tabs.length ? this.host.tabs.map(t => [t.label, t.name]) : [['', 'COTI']];
    for (const [label, name] of names) {
      const row = document.createElement('label');
      row.className = 'coti-check';
      const box = document.createElement('input');
      box.type = 'checkbox';
      box.dataset.tube = label;
      box.checked = this.visible[label] !== false;
      box.onchange = () => {
        this.visible[label] = box.checked;
        this.apply();
        this.drawScreen();
      };
      row.append(box, name);
      this.visPanel.appendChild(row);
    }

    const kept = labels.includes(this.tube);
    this.select(kept ? this.sel : null, kept ? this.tube : this.host.home);
  }

  // The Type selector: the device under another layout, as the page computed it. Poses and text blocks held in memory
  // stay, so switching back loses nothing, and tubes the page seeded are added. A copy, so the table keeps the
  // server's view and a discarded override is gone the next time the device is opened.
  setLayout(view) {
    const { tubes, texts, ...rest } = view;
    this.host = { ...this.host, ...rest };
    for (const [label, mount] of Object.entries(tubes)) {
      if (!(label in this.mounts)) this.mounts[label] = mount;
    }
    for (const [label, text] of Object.entries(texts)) {
      if (!(label in this.texts)) this.texts[label] = text;
    }
    this.showTubes();
    this.loadScreen();
    this.onDirty(this.isDirty());
  }

  // After a save or a revert: the devices as the server now holds them. The camera, flip, checkboxes and selection
  // stay.
  refreshHosts(hosts, hostId) {
    this.hosts = hosts;
    this.host = hosts[hostId];
    this.mounts = structuredClone(this.host.tubes);
    this.original = structuredClone(this.host.tubes);
    this.texts = structuredClone(this.host.texts);
    this.originalTexts = structuredClone(this.host.texts);
    this.showTubes();
    this.loadScreen();
    this.onDirty(false);
  }

  // selection

  // kind: 'coti', 'text' or null for the device. The tabs, the 3D view, the screen and the inspector all follow.
  select(kind, label) {
    const changed = (kind !== null) !== (this.sel !== null);
    this.sel = kind;
    this.tube = label;
    // An unposed tube shows where it sits in game, the legacy mount, until its first edit.
    this.mount = this.mounts[label] || structuredClone(this.host.mount);
    this.apply();
    this.refresh();
    this.paintTubes();
    this.paintText();
    this.drawScreen();
    if (changed) this.onSelect(kind !== null);
  }

  // A tab picks the tube and keeps what is selected of it.
  selectTube(label) {
    this.select(this.sel ?? 'coti', label);
  }

  // The panel shows the device's view controls always, and the selected item's properties.
  paintPanel() {
    const show = (sel, on) => this.panel.querySelectorAll(sel).forEach(el => el.hidden = !on);
    show('.coti-forsel', this.sel !== null);
    show('.coti-forcoti', this.sel === 'coti');
    show('.coti-fortext', this.sel === 'text');
  }

  // The tube tabs, and what the selected tube carries. A v1 device has no tabs, only its stored circle.
  paintTubes() {
    this.paintPanel();
    const tabs = this.panel.querySelector('#c-tubes');
    const note = this.panel.querySelector('#c-tubenote');
    const mirror = this.panel.querySelector('#c-mirror');
    const tab = this.host.tabs.find(t => t.label === this.tube);

    tabs.innerHTML = '';
    for (const t of this.host.tabs) {
      const b = document.createElement('button');
      // The panel is narrow, so the button shows the tube number and the title the rest.
      b.textContent = t.label.replace('tube_', '');
      b.title = `${t.name}, slot ${t.slot}`;
      b.className = (t.label === this.tube ? 'on ' : '') + (t.label in this.mounts ? '' : 'unposed');
      b.onclick = () => this.selectTube(t.label);
      tabs.appendChild(b);
    }

    mirror.hidden = !tab?.partner;
    mirror.disabled = !tab?.partner || !(tab.partner in this.mounts);
    mirror.textContent = tab?.partner ? `Mirror from ${tab.partner}` : 'Mirror from partner';

    // A file without a layout shows its one COTI at the legacy mount, and nothing is editable until a type is picked.
    const locked = !tab;
    this.panel.querySelectorAll('.coti-edit button, .coti-edit input, .coti-edit select, #c-anchor')
      .forEach(b => b.disabled = locked);
    if (locked) {
      note.textContent = 'This file has no layout. Pick a type to edit its tubes.';
      return;
    }

    const pod = tab.pod
      ? `Pod on ${tab.pod.bone}, down at ${tab.pod.downX}, ${tab.pod.downY}, ${tab.pod.downZ}.`
      : 'No pod: it follows the goggles.';
    note.textContent = `${tab.label}, slot ${tab.slot}. ` +
      (tab.label in this.mounts ? '' : 'Not posed: in game it mounts at the legacy mount. ') + pod;
  }

  // Fills this tube from its partner across the centre line. CotiTubeMirror does the sums on the server, so the
  // rule exists once.
  async mirrorFromPartner() {
    const tab = this.host.tabs.find(t => t.label === this.tube);
    const source = tab?.partner && this.mounts[tab.partner];
    if (!source) return;
    const anchor = this.mounts[this.tube]?.anchorBone || null;
    this.mount = JSON.parse(await this.mirror(JSON.stringify(source), anchor));
    this.edited();
  }

  // Every edit lands here: the tube counts as posed from now on, and the view and the dirty flag follow.
  edited() {
    this.mounts[this.tube] = this.mount;
    this.apply();
    this.refresh();
    this.paintTubes();
    this.onDirty(this.isDirty());
  }

  // text

  // The circle a tube's text sits in, on the screen shown.
  circleOf(label) {
    return this.host.preview[this.aspect].circles.find(c => c.label === label);
  }

  // Where a tube's text sits: each field its block sets, else the rule's, as CotiDisplayLayout.Resolve does. The
  // default align comes from the server with each circle.
  placement(label, texts = this.texts) {
    const b = texts[label] || {};
    const align = b.align || this.circleOf(label)?.align || 'center';
    const rule = this.host.textRule;
    return { align, edge: b.edge ?? (align === 'center' ? 0 : rule.edge), y: b.y ?? 0 };
  }

  // Moves a tube's text to a placement, held at the screen's margin and snapped to the step. The align the block names
  // is kept: only the inspector changes it.
  placeText(label, p) {
    const c = this.circleOf(label);
    const aspect = this.host.preview[this.aspect].width / this.host.preview[this.aspect].height;
    const held = placementOf(textRect(c, p, aspect, this.host.textRule), c, p.align, aspect);
    this.texts[label] = { ...this.texts[label], edge: snap(held.edge), y: snap(held.y) };
    this.textEdited(label);
  }

  // A move across the screen in radii, right and up positive: a left-anchored edge's distance shrinks moving right.
  moveText(label, from, dx, dy) {
    const edge = from.align === 'left' ? from.edge - dx : from.edge + dx;
    this.placeText(label, { align: from.align, edge, y: from.y + dy });
  }

  // Every text edit lands here. A tube is saved only with a mount, so a text edit poses an unposed tube at the legacy
  // mount, where it already sits in game, as its first mount edit would.
  textEdited(label = this.tube) {
    if (!(label in this.mounts)) {
      this.mounts[label] = structuredClone(this.host.mount);
      if (label === this.tube) this.mount = this.mounts[label];
      this.paintTubes();
    }
    this.paintText();
    this.drawScreen();
    this.onDirty(this.isDirty());
  }

  // Arrow keys move the selected text by a step, Shift by ten, unless the focus is on the page outside the viewer.
  nudgeText(e) {
    if (e.target !== document.body && !this.viewport.contains(e.target) && !this.panel.contains(e.target)) return;
    if (this.sel !== 'text' || !this.host.tabs.length || /^(INPUT|SELECT|TEXTAREA)$/.test(e.target?.tagName)
        || e.target?.closest?.('.mud-popover, .mud-select, [role=listbox]')) return;
    const by = e.shiftKey ? 0.1 : 0.01;
    const move = { ArrowLeft: [-by, 0], ArrowRight: [by, 0], ArrowUp: [0, by], ArrowDown: [0, -by] }[e.key];
    if (!move) return;
    e.preventDefault();
    this.moveText(this.tube, this.placement(this.tube), move[0], move[1]);
  }

  // The inspector's text fields: the block's own values, blank where the rule decides, which the placeholder shows.
  paintText() {
    const b = this.texts[this.tube] || {};
    const p = this.placement(this.tube);
    const def = this.circleOf(this.tube)?.align || 'center';
    const align = this.panel.querySelector('#c-talign');
    align.options[0].textContent = `default (${def})`;
    align.value = b.align || '';
    const edge = this.panel.querySelector('#c-tedge');
    edge.value = b.edge ?? '';
    edge.placeholder = (p.align === 'center' ? 0 : this.host.textRule.edge).toFixed(2);
    const y = this.panel.querySelector('#c-ty');
    y.value = b.y ?? '';
    y.placeholder = '0.00';
    this.panel.querySelector('#c-tnote').textContent =
      `${this.tube || 'COTI'}: ${p.align}, edge ${p.edge.toFixed(2)}, y ${p.y.toFixed(2)} radii.`;
  }

  // A field of the selected text's block from the inspector; blank or 'default' removes it, so the rule decides.
  setText(field, value) {
    if (!this.host.tabs.length) return;
    const b = { ...this.texts[this.tube] };
    if (value === '' || value === null || Number.isNaN(value)) delete b[field];
    else b[field] = field === 'align' ? value : snap(value);
    this.texts[this.tube] = Object.keys(b).length ? b : null;
    this.textEdited();
  }

  // screen

  // The text image arrives after the first draw, which it repeats.
  loadScreen() {
    if (this.textImageUrl !== this.host.textImage) {
      this.textImageUrl = this.host.textImage;
      this.textImage = null;
      if (this.host.textImage) {
        this.textImage = new Image();
        this.textImage.onload = () => this.drawScreen();
        this.textImage.src = this.host.textImage;
      }
    }

    this.screenSizes.innerHTML = '';
    this.host.preview.forEach((p, i) => {
      const b = document.createElement('button');
      b.textContent = p.name;
      b.title = `${p.width} x ${p.height}`;
      b.className = i === this.aspect ? 'on' : '';
      b.onclick = () => {
        this.aspect = i;
        [...this.screenSizes.children].forEach((c, j) => c.classList.toggle('on', j === i));
        this.paintText();
        this.drawScreen();
      };
      this.screenSizes.appendChild(b);
    });
    const big = document.createElement('button');
    big.textContent = this.screenWrap.classList.contains('big') ? 'Shrink' : 'Enlarge';
    big.onclick = () => {
      big.textContent = this.screenWrap.classList.toggle('big') ? 'Shrink' : 'Enlarge';
    };
    this.screenSizes.appendChild(big);
    this.drawScreen();
  }

  // What the screen shows, in canvas pixels (rows down): each shown tube's circle and its text's rectangle.
  screenItems() {
    const p = this.host.preview[this.aspect];
    const W = p.width;
    const H = p.height;
    return p.circles.filter(k => this.visible[k.label] !== false).map(k => {
      const r = textRect(k, this.placement(k.label), W / H, this.host.textRule);
      return { circle: k, text: { x: r.x * W, y: (1 - r.y - r.h) * H, w: r.w * W, h: r.h * H } };
    });
  }

  // The item under a canvas point: a text before a circle, since texts sit inside circles.
  hitScreen(x, y) {
    const items = this.screenItems();
    const H = this.host.preview[this.aspect].height;
    const W = this.host.preview[this.aspect].width;
    const text = items.find(({ text: t }) => x >= t.x && x <= t.x + t.w && y >= t.y && y <= t.y + t.h);
    if (text) return { kind: 'text', label: text.circle.label };
    const circle = items.find(({ circle: k }) => Math.hypot(x - k.u * W, y - (1 - k.v) * H) <= k.r * H);
    return circle ? { kind: 'coti', label: circle.circle.label } : null;
  }

  initScreenInput() {
    const at = e => {
      const box = this.screen.getBoundingClientRect();
      return [(e.clientX - box.left) * this.screen.width / box.width, (e.clientY - box.top) * this.screen.height / box.height];
    };

    this.screen.addEventListener('pointerdown', e => {
      if (e.button !== 0) return;
      const [x, y] = at(e);
      const hit = this.hitScreen(x, y);
      this.select(hit?.kind ?? null, hit?.label ?? this.tube);
      // Dragging the text moves it from where it sat when pressed; the align never changes.
      if (hit?.kind === 'text' && this.host.tabs.length) {
        this.drag = { x, y, from: this.placement(hit.label) };
        this.screen.setPointerCapture(e.pointerId);
      }
    });

    this.screen.addEventListener('pointermove', e => {
      const [x, y] = at(e);
      if (!this.drag) {
        const hit = this.hitScreen(x, y);
        this.screen.style.cursor = hit?.kind === 'text' ? 'move' : hit ? 'pointer' : 'default';
        return;
      }
      // Canvas pixels to radii: a pixel is 1 / H screen heights, and rows run down.
      const r = this.circleOf(this.tube).r * this.host.preview[this.aspect].height;
      this.moveText(this.tube, this.drag.from, (x - this.drag.x) / r, -(y - this.drag.y) / r);
    });

    const end = () => { this.drag = null; };
    this.screen.addEventListener('pointerup', end);
    this.screen.addEventListener('pointercancel', end);
  }

  // A game screen: every shown circle, the selected COTI's highlighted, and the longest message in each, placed as in
  // game. A selected text is outlined, with the screen margin it cannot cross.
  drawScreen() {
    const p = this.host.preview[this.aspect];
    const W = p.width;
    const H = p.height;
    this.screen.width = W;
    this.screen.height = H;
    const g = this.screen.getContext('2d');

    g.fillStyle = '#23402a';
    g.fillRect(0, 0, W, H);

    const items = this.screenItems();

    // Viewport v runs up and canvas rows run down. A hidden tube's circle is left out, as its COTI is.
    for (const { circle: k } of items) {
      const on = this.sel === 'coti' && k.label === this.tube;
      g.beginPath();
      g.arc(k.u * W, (1 - k.v) * H, k.r * H, 0, Math.PI * 2);
      g.fillStyle = on ? 'rgba(217, 119, 42, 0.3)' : 'rgba(154, 165, 180, 0.18)';
      g.fill();
      g.lineWidth = on ? 6 : 3;
      g.strokeStyle = on ? '#d9772a' : '#9aa5b4';
      g.stroke();
    }

    // White letters on black: adding the image leaves only the letters.
    const t = this.textImage;
    if (t?.complete && t.naturalWidth) {
      g.globalCompositeOperation = 'lighter';
      for (const { text: r } of items) g.drawImage(t, r.x, r.y, r.w, r.h);
      g.globalCompositeOperation = 'source-over';
    }

    const selected = this.sel === 'text' && items.find(i => i.circle.label === this.tube);
    if (selected) {
      const m = this.host.textRule.margin * H;
      g.setLineDash([18, 12]);
      g.lineWidth = 3;
      g.strokeStyle = 'rgba(217, 119, 42, 0.7)';
      g.strokeRect(m, m, W - 2 * m, H - 2 * m);
      g.setLineDash([]);
      g.lineWidth = 5;
      g.strokeStyle = '#d9772a';
      const r = selected.text;
      g.strokeRect(r.x - 6, r.y - 6, r.w + 12, r.h + 12);
    }
  }

  // Fits both objects to the viewport. Distance comes from the bounding sphere and the vertical
  // field of view.
  frame(wide = false) {
    // Centred on the selected tube's COTI, not the assembly.
    const device = new THREE.Box3().setFromObject(this.cotis.get(this.tube)).getBoundingSphere(new THREE.Sphere());
    const sphere = wide
      ? new THREE.Box3().setFromObject(this.hostGroup)
          .union(new THREE.Box3().setFromObject(this.pivotNode))
          .union(new THREE.Box3().setFromObject(this.cotiGroup)).getBoundingSphere(new THREE.Sphere())
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

  // The one place the pose is computed. bone -> mount -> world, for every tube's COTI.
  apply() {
    this.pivotNode.matrix.copy(this.boneMatrix(this.host.pivot));
    this.pivotNode.updateMatrixWorld(true);

    for (const [label, node] of this.cotis) {
      // An unposed tube shows where it sits in game, the legacy mount.
      const m = label === this.tube ? this.mount : (this.mounts[label] || this.host.mount);
      node.matrix.copy(this.boneMatrix(m.anchorBone)).multiply(new THREE.Matrix4().compose(
        toThreeVec([m.positionX || 0, m.positionY || 0, m.positionZ || 0]),
        toThreeQuat(mountQuat(m).toArray()),
        new THREE.Vector3().setScalar(m.scale > 0 ? m.scale : 1)));
      node.visible = this.visible[label] !== false;
      const colour = this.sel === 'coti' && label === this.tube ? COTI_ON : COTI_OFF;
      node.traverse(o => { if (o.isMesh) o.material.color.setHex(colour); });
    }
    this.cotiGroup.updateMatrixWorld(true);
    this.drawAxes();
  }

  // A bone's world transform; an unknown or empty name means the pivot. The moving mesh was exported about the pivot,
  // so only the pivot takes the flip: a COTI on another bone, like the Chimera's left pod in the static mesh, stays.
  boneMatrix(name) {
    const key = name && this.host.bones[name] ? name : this.host.pivot;
    const bone = this.host.bones[key] || { pos: [0, 0, 0], quat: [0, 0, 0, 1] };
    const turn = new THREE.Quaternion();
    if (key === this.host.pivot) {
      turn.setFromAxisAngle(new THREE.Vector3(1, 0, 0), THREE.MathUtils.degToRad(this.flip));
    }
    return new THREE.Matrix4().compose(
      toThreeVec(bone.pos), toThreeQuat(bone.quat).multiply(turn), new THREE.Vector3(1, 1, 1));
  }

  // A gnomon at the selected COTI's origin.
  drawAxes() {
    this.axes.clear();
    const node = this.cotis.get(this.tube);
    if (!this.showAxes || this.sel !== 'coti' || !this.frameRadius || !node?.visible) return;
    const len = this.frameRadius * 0.22;
    const origin = new THREE.Vector3().setFromMatrixPosition(node.matrixWorld);
    // Scale is taken out first; a rotation read off a scaled matrix is not a rotation.
    const rot = new THREE.Quaternion();
    node.matrixWorld.decompose(new THREE.Vector3(), rot, new THREE.Vector3());
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
    if (!this.host.tabs.length) return;
    const s = STEPS[this.step];
    const by = field === 'scale' ? s.scale : (field.startsWith('position') ? s.pos : s.ang);
    const dp = field === 'scale' ? 4 : (field.startsWith('position') ? 4 : 2);
    this.mount[field] = +(((this.mount[field] || 0) + dir * by).toFixed(dp));
    this.edited();
  }

  set(field, value) {
    if (!this.host.tabs.length) return;
    this.mount[field] = value;
    this.edited();
  }

  // Only the layout's tubes count: a pose or text kept in memory for another layout is not saved. A text block counts
  // by where it places the text, as the save trims it to what differs from the rule.
  isDirty() {
    const placed = (texts, l) => JSON.stringify(this.placement(l, texts));
    return this.labels().some(l => JSON.stringify(this.mounts[l]) !== JSON.stringify(this.original[l])
      || placed(this.texts, l) !== placed(this.originalTexts, l));
  }

  getMounts() { return structuredClone(this.mounts); }

  getTexts() { return structuredClone(this.texts); }

  buildPanel() {
    this.panel.innerHTML = `
      <div class="coti-sec coti-forsel">
        <div class="coti-sec-h">Tube</div>
        <div class="coti-steps" id="c-tubes"></div>
        <div class="coti-readout" id="c-tubenote" style="text-align:left"></div>
        <div class="coti-frame coti-forcoti" style="margin-top:7px; grid-template-columns:1fr">
          <button id="c-mirror">Mirror from partner</button>
        </div>
        <div class="coti-frame" style="margin-top:7px; grid-template-columns:1fr">
          <button id="c-deselect">Done: back to the device</button>
        </div>
      </div>

      <div class="coti-sec coti-edit coti-fortext">
        <div class="coti-sec-h">Text <span class="coti-unit">circle radii</span></div>
        <div class="coti-field"><span class="coti-lab">Align</span>
          <select id="c-talign" class="coti-select">
            <option value="">default</option>
            <option value="left">left</option>
            <option value="right">right</option>
            <option value="center">center</option>
          </select>
        </div>
        <div class="coti-field"><span class="coti-lab">Edge</span>
          <input type="number" step="0.01" id="c-tedge" class="coti-select"></div>
        <div class="coti-field"><span class="coti-lab">Y</span>
          <input type="number" step="0.01" id="c-ty" class="coti-select"></div>
        <div class="coti-frame" style="margin-top:7px; grid-template-columns:1fr">
          <button id="c-treset">Reset to default</button>
        </div>
        <div class="coti-readout" id="c-tnote"></div>
        <div class="coti-readout" style="text-align:left">
          Drag the text in the screen, or use the arrow keys (Shift for 0.1). Blank fields follow the rule.
        </div>
      </div>

      <div class="coti-sec coti-forcoti">
        <div class="coti-sec-h">Anchor</div>
        <select id="c-anchor" class="coti-select"></select>
        <div class="coti-sec-h" style="margin-top:11px">Step</div>
        <div class="coti-steps" id="c-steps"></div>
      </div>

      <div class="coti-sec coti-edit coti-forcoti">
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

      <div class="coti-sec coti-edit coti-forcoti">
        <div class="coti-sec-h">Rotation <span class="coti-unit">degrees</span></div>
        <div id="c-rot"></div>
      </div>

      <div class="coti-sec coti-edit coti-forcoti">
        <div class="coti-sec-h">Scale</div>
        <div id="c-scale"></div>
      </div>

      <div class="coti-sec">
        <div class="coti-frame">
          <button id="c-frame">Frame COTI</button>
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
        <div id="c-flipsec">
          <div class="coti-sec-h" style="margin-top:11px">Flip <span class="coti-unit" id="c-fliplab">0&deg;</span></div>
          <input type="range" id="c-flip" class="coti-range" min="-120" max="120" step="1" value="0">
          <div class="coti-frame" style="margin-top:7px">
            <button id="c-anim">Animate</button>
            <button id="c-flipreset">Rest</button>
          </div>
        </div>
        <label class="coti-check" style="margin-top:11px"><input type="checkbox" id="c-axes" checked> Show axis gnomon</label>
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

    this.panel.querySelector('#c-mirror').onclick = () => this.mirrorFromPartner();
    this.panel.querySelector('#c-deselect').onclick = () => this.select(null, this.tube);

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

    const talign = this.panel.querySelector('#c-talign');
    talign.onchange = () => this.setText('align', talign.value);
    for (const [id, field] of [['#c-tedge', 'edge'], ['#c-ty', 'y']]) {
      const input = this.panel.querySelector(id);
      input.onchange = () => this.setText(field, input.value === '' ? '' : +input.value);
    }
    this.panel.querySelector('#c-treset').onclick = () => {
      if (!this.host.tabs.length) return;
      this.texts[this.tube] = null;
      this.textEdited();
    };

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
    removeEventListener('resize', this.onResize);
    removeEventListener('keydown', this.onKey);
    this.renderer.setAnimationLoop(null);
    this.cube.dispose();
    this.renderer.dispose();
    this.viewport.innerHTML = '';
    this.panel.innerHTML = '';
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

    // Only a host with a separately exported moving half can flip.
    const flipSection = this.panel.querySelector('#c-flipsec');
    if (flipSection) flipSection.hidden = !this.host.hasPivotMesh;

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

const STEPS = [
  { name: 'Fine', pos: 0.0005, ang: 0.5, scale: 0.005 },
  { name: 'Normal', pos: 0.002, ang: 2, scale: 0.02 },
  { name: 'Coarse', pos: 0.01, ang: 10, scale: 0.1 },
];
