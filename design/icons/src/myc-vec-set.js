/* myc-vec-set.js — one def() per glyph, grouped by ribbon panel.
 * Requires myc-vec.js. Every mass here uses the same isometric; plan views are
 * used only where the subject genuinely is a footprint operation.
 */
(function (V) {
  'use strict';
  var def = V.def;

  /* ---- assembly mark -------------------------------------------------- */
  def({
    name: 'Mycelium', component: 'MyceliumInfo (assembly mark)', panel: '—',
    family: 'built', motif: 'urban field + data roots', badge: null,
    note: 'A compact urban morphology connected by Grasshopper-like mycelial data wires. Monochrome and distinct from MyceliumMassing.',
    draw: function (k) {
      var ink = '#202020', p = k.iso(12.0, 6.0), s = [];
      s = s.concat(k.M.plate(p, k.rect(0, 0, 8.0, 8.0), 0.55,
        { ink: ink, fill: k.PAL.white, wall: ink }));
      var d = p.lift(0.55);
      [
        [0.4, 0.5, 2.7, 2.7, 2.2],
        [3.1, 0.5, 5.4, 2.4, 3.7],
        [5.8, 0.5, 7.6, 2.8, 2.6],
        [0.5, 3.3, 2.4, 5.7, 3.0],
        [3.0, 3.0, 5.1, 5.1, 2.0],
        [5.6, 3.4, 7.5, 5.7, 3.2],
        [2.0, 6.0, 4.4, 7.5, 2.3]
      ].forEach(function (b) {
        s = s.concat(k.M.mass(d, k.rect(b[0], b[1], b[2], b[3]), b[4],
          { ink: ink, wall: ink, top: k.PAL.white }));
      });

      var wires = [
        [[12.0, 14.0], [10.0, 15.1], [7.7, 15.4], [5.2, 15.0]],
        [[12.0, 14.0], [9.4, 16.0], [7.1, 17.1], [5.4, 18.8]],
        [[12.0, 14.0], [11.0, 16.4], [9.8, 18.2], [9.4, 20.5]],
        [[12.0, 14.0], [12.0, 16.7], [12.2, 19.1], [12.4, 21.6]],
        [[12.0, 14.0], [13.2, 16.1], [15.1, 17.0], [16.7, 18.7]],
        [[12.0, 14.0], [14.6, 15.2], [17.2, 15.1], [19.5, 14.7]]
      ];
      s = s.concat(k.M.roots(wires, { ink: ink }));
      [[5.2, 15.0], [5.4, 18.8], [9.4, 20.5], [12.4, 21.6], [16.7, 18.7], [19.5, 14.7]].forEach(function (n) {
        s.push(k.disc(n[0], n[1], 0.72, { fill: k.PAL.white, stroke: ink, w: k.W.hair }));
      });
      s.push(k.disc(8.1, 16.6, 0.46, { fill: ink }));
      s.push(k.disc(15.0, 16.7, 0.46, { fill: ink }));
      return s;
    }
  });

  /* ---- panel: Massing --------------------------------------------------- */
  def({
    name: 'MyceliumMassing', component: 'Massing Generator', panel: 'Massing',
    family: 'ground', motif: 'field + mass', badge: 'grid',
    note: 'The generator, not the result: a soil parcel field, two cells risen.',
    draw: function (k) {
      var p = k.iso(11.0, 6.4);
      var s = k.M.field(p, k.rect(0, 0, 9.6, 9.6), 0.8,
        [[4.8, 0, 4.8, 9.6], [0, 4.8, 9.6, 4.8]], { fill: k.PAL.soil, wall: k.PAL.soil });
      s = s.concat(k.M.mass(p, k.rect(0, 0, 4.8, 4.8), 5.4, {}));
      return s.concat(k.M.mass(p, k.rect(4.8, 0, 9.6, 4.8), 3.2, {}));
    }
  });

  /* ---- panel: Building Types -------------------------------------------- */
  def({
    name: 'MyceliumCourtyard', component: 'Courtyard Config', panel: 'Building Types',
    family: 'built', motif: 'mass (void)', badge: 'gear',
    note: 'Isometric ring around a void; the void is the subject.',
    draw: function (k) {
      var p = k.iso(11.6, 5.6);
      return k.M.mass(p, k.rect(0, 0, 10.2, 10.2), 4.6, { hole: k.rect(2.6, 2.6, 7.6, 7.6) });
    }
  });

  def({
    name: 'MyceliumLinear', component: 'Linear Config', panel: 'Building Types',
    family: 'built', motif: 'mass', badge: 'gear',
    note: 'One long bar, clearly longer than it is deep.',
    draw: function (k) {
      var p = k.iso(12.3, 9.3);
      return k.M.mass(p, k.rect(0, 0, 3.8, 10.5), 4.6, {});
    }
  });

  def({
    name: 'MyceliumPoint', component: 'Point Config', panel: 'Building Types',
    family: 'built', motif: 'mass', badge: 'gear',
    note: 'A single compact block, square in plan. No interior void at all.',
    draw: function (k) {
      var p = k.iso(9.0, 10.2);
      return k.M.mass(p, k.rect(0, 0, 6.4, 6.4), 7.0, {});
    }
  });

  def({
    name: 'MyceliumL', component: 'L-Shape Config', panel: 'Building Types',
    family: 'built', motif: 'mass', badge: 'gear',
    note: 'Isometric L with the corner facing the viewer.',
    draw: function (k) {
      var p = k.iso(11.0, 6.0);
      return k.M.mass(p, [[4.2, 0], [9.6, 0], [9.6, 9.6], [0, 9.6], [0, 4.2], [4.2, 4.2]], 5.0, {});
    }
  });

  def({
    name: 'MyceliumU', component: 'U-Shape Config', panel: 'Building Types',
    family: 'built', motif: 'mass', badge: 'gear',
    note: 'Isometric U opening toward the viewer, clear of the badge.',
    draw: function (k) {
      var p = k.iso(9.8, 5.5);
      return k.M.mass(p, [[0, 0], [10.2, 0], [10.2, 3.1], [4.3, 3.1],
                          [4.3, 6.3], [10.2, 6.3], [10.2, 9.4], [0, 9.4]], 4.6, {});
    }
  });

  def({
    name: 'MyceliumTower', component: 'Tall Building Config', panel: 'Building Types',
    family: 'built', motif: 'mass (plates)', badge: 'gear',
    note: 'Tall and slender, 0.85 floor-plate hairlines. Verticality is the whole message.',
    draw: function (k) {
      var p = k.iso(8.2, 13.85);
      return k.M.mass(p, k.rect(0, 0, 4.6, 4.6), 13.0, { plates: 2.6 });
    }
  });

  /* ---- panel: Vegetation ------------------------------------------------- */
  def({
    name: 'MyceliumTree', component: 'Tree Config', panel: 'Vegetation',
    family: 'plant', motif: 'canopy', badge: 'gear',
    note: 'One tree standing on isometric ground so it sits beside the masses credibly.',
    draw: function (k) {
      var p = k.iso(9.0, 12.8);
      return k.M.canopy(p, k.rect(0, 0, 4.8, 4.8), { r: 3.9, rise: 5.4, trunk: 1.5 });
    }
  });

  def({
    name: 'MyceliumGreenNetwork', component: 'Green Network Generator', panel: 'Vegetation',
    family: 'plant', motif: 'field', badge: null,
    note: 'A connected sage field: perimeter band, crossing corridor, and refuge node.',
    draw: function (k) {
      var p = k.iso(11.0, 7.0);
      return k.M.field(p, k.rect(0, 0, 9.6, 9.6), 0.8,
        [[1.8, 4.8, 7.8, 4.8], [4.8, 1.8, 4.8, 7.8]],
        { fill: k.PAL.sage, wall: k.PAL.sage });
    }
  });

  /* ---- panel: Site -------------------------------------------------------- */
  def({
    name: 'MyceliumTerrain', component: 'Terrain Generator', panel: 'Site',
    family: 'ground', motif: 'terrain', badge: null,
    note: 'A noise-displaced ground sample — a subtly undulating field, not a mountain range.',
    draw: function (k) {
      var p = k.iso(12.0, 6.6);
      var hf = function (u, v) { return 1.7 + 0.85 * Math.sin(u * 0.62 + 0.4) + 0.65 * Math.cos(v * 0.58 - 0.5); };
      return k.M.terrain(p, 11, hf, 2.4, { fill: k.PAL.soil, contours: [2.8, 5.5, 8.2] });
    }
  });

  /* ---- panel: Utilities ---------------------------------------------------- */
  def({
    name: 'MyceliumTemplate', component: 'Mycelium Templates', panel: 'Utilities',
    family: 'tool', motif: 'stack', badge: 'down',
    note: 'Versioned definition cards being fetched. Slate so tooling recedes. No third-party marks.',
    draw: function (k) {
      var p = k.iso(9.8, 8.2);
      return k.M.stack(p, k.rect(0, 0, 9.0, 6.8), 3, 1.95, 0.6, {
        ink: k.accent, wall: k.PAL.white, rule: k.accent,
        rules: [[1.6, 1.8, 7.4, 1.8], [1.6, 3.5, 5.6, 3.5]]
      });
    }
  });
})(window.MYC);
