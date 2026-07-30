/* myc-vec.js — Mycelium icon engine.
 * 24x24 unit space, 30deg isometric, two stroke weights, four family accents.
 * Every glyph is built from a motif in M plus at most one badge from B, and
 * every glyph goes through V.glyph() so the safe-area guard always runs.
 * No dependencies. Exposes window.MYC (and module.exports under CommonJS).
 */
(function (root) {
  'use strict';

  var C = Math.sqrt(3) / 2;          // cos(30deg) — the isometric horizontal scale

  var PAL = {
    pine:  '#29473A',
    sage:  '#7E9469',
    soil:  '#6B4F35',
    slate: '#5A6660',
    cream: '#F1EDE1',
    white: '#FFFFFF',
    /* derived neutrals: pine over white at 12% / 26%. Not brand colours —
       they exist only so an isometric mass reads as a mass. */
    shadeA: '#E7EAE8',
    shadeB: '#C9D1CD'
  };
  PAL.ink = PAL.pine;

  var FAMILY = { built: PAL.pine, ground: PAL.soil, plant: PAL.sage, tool: PAL.slate };
  var W = { contour: 1.25, hair: 0.85 };
  var SAFE = { min: 0.7, max: 23.3 };
  var BADGE = { cx: 17, cy: 17, r: 4.4 };
  var TINT_MIN = 3.5;                // filled detail narrower than this is tinted, not inked

  function r3(n) { return Math.round(n * 1000) / 1000; }

  /* ---- projection -------------------------------------------------- */
  /* u -> lower right, v -> lower left, w -> up. Viewer above-left. */
  function iso(ox, oy) {
    var f = function (u, v, w) { return [ox + (u - v) * C, oy + (u + v) * 0.5 - (w || 0)]; };
    f.ox = ox; f.oy = oy;
    f.lift = function (dz) { return iso(ox, oy - dz); };
    return f;
  }

  function rect(u0, v0, u1, v1) { return [[u0, v0], [u1, v0], [u1, v1], [u0, v1]]; }

  /* ---- shapes ------------------------------------------------------- */
  function dPoly(pts, close) {
    var d = pts.map(function (p, i) { return (i ? 'L' : 'M') + r3(p[0]) + ' ' + r3(p[1]); }).join(' ');
    return close === false ? d : d + ' Z';
  }
  function S(d, pts, o) {
    var s = { type: 'path', d: d, pts: pts, fill: 'none', stroke: 'none', w: 0, rule: 'nonzero' };
    for (var k in o) if (o.hasOwnProperty(k)) s[k] = o[k];
    return s;
  }
  function disc(cx, cy, r, o) {
    var s = { type: 'circle', cx: cx, cy: cy, r: r, pts: [[cx - r, cy - r], [cx + r, cy + r]],
              fill: 'none', stroke: 'none', w: 0 };
    for (var k in o) if (o.hasOwnProperty(k)) s[k] = o[k];
    return s;
  }
  function line(a, b, o) { return S(dPoly([a, b], false), [a, b], o); }
  function curve(pts, o) {
    // pts: [p0, c1, c2, p1, c1, c2, p1, ...]
    var d = 'M' + r3(pts[0][0]) + ' ' + r3(pts[0][1]);
    for (var i = 1; i + 2 < pts.length + 1; i += 3) {
      d += ' C' + [pts[i], pts[i + 1], pts[i + 2]].map(function (p) { return r3(p[0]) + ' ' + r3(p[1]); }).join(' ');
    }
    return S(d, pts, o);
  }

  /* ---- extrusion ----------------------------------------------------- */
  function signedArea(poly) {
    var a = 0;
    for (var i = 0; i < poly.length; i++) {
      var p = poly[i], q = poly[(i + 1) % poly.length];
      a += p[0] * q[1] - q[0] * p[1];
    }
    return a / 2;
  }
  /* outward normal of every edge, in plan space */
  function normals(poly) {
    var s = signedArea(poly) > 0 ? 1 : -1;
    return poly.map(function (p, i) {
      var q = poly[(i + 1) % poly.length], dx = q[0] - p[0], dy = q[1] - p[1];
      return s > 0 ? [dy, -dx] : [-dy, dx];
    });
  }
  /* a face is seen when its plan normal points toward the viewer: nu + nv > 0 */
  function eachVisibleEdge(poly, invert, fn) {
    var ns = normals(poly);
    for (var i = 0; i < poly.length; i++) {
      var n = invert ? [-ns[i][0], -ns[i][1]] : ns[i];
      if (n[0] + n[1] <= 1e-9) continue;
      fn(poly[i], poly[(i + 1) % poly.length], n[0] > n[1] ? 'right' : 'left');
    }
  }

  function walls(pj, poly, top, o) {
    o = o || {};
    var base = o.base === undefined ? 0 : o.base, out = [];
    eachVisibleEdge(poly, o.invert, function (p, q, side) {
      var pts = [pj(p[0], p[1], top), pj(q[0], q[1], top), pj(q[0], q[1], base), pj(p[0], p[1], base)];
      out.push(S(dPoly(pts), pts, {
        fill: o.fill || (side === 'right' ? PAL.shadeB : PAL.shadeA),
        stroke: o.ink || PAL.ink, w: W.contour
      }));
    });
    return out;
  }

  function face(pj, poly, h, o) {
    o = o || {};
    var pts = poly.map(function (p) { return pj(p[0], p[1], h); });
    var d = dPoly(pts), all = pts.slice();
    if (o.hole) {
      var hp = o.hole.map(function (p) { return pj(p[0], p[1], h); });
      d += ' ' + dPoly(hp);
      all = all.concat(hp);
    }
    return S(d, all, {
      fill: o.fill || PAL.white, stroke: o.ink || PAL.ink,
      w: o.w || W.contour, rule: o.hole ? 'evenodd' : 'nonzero'
    });
  }

  /* 0.85 hairlines wrapped around the visible faces at regular heights */
  function floorPlates(pj, poly, top, step, o) {
    o = o || {};
    var out = [];
    for (var w = step; w < top - 0.25; w += step) {
      (function (h) {
        eachVisibleEdge(poly, o.invert, function (p, q) {
          out.push(line(pj(p[0], p[1], h), pj(q[0], q[1], h), { stroke: o.ink || PAL.ink, w: W.hair }));
        });
      })(w);
    }
    return out;
  }

  /* ---- motif library M ----------------------------------------------- */
  var M = {};

  /* mass — the workhorse: any plan polygon extruded, optionally with a void */
  M.mass = function (pj, plan, h, o) {
    o = o || {};
    var s = [];
    if (o.hole) {
      s.push(face(pj, o.hole, 0, { fill: o.floor || PAL.white, ink: o.ink }));
      s = s.concat(walls(pj, o.hole, h, { invert: true, ink: o.ink, fill: o.holeWall || PAL.shadeB }));
    }
    s = s.concat(walls(pj, plan, h, { ink: o.ink, fill: o.wall }));
    if (o.plates) s = s.concat(floorPlates(pj, plan, h, o.plates, { ink: o.ink }));
    s.push(face(pj, plan, h, { hole: o.hole, fill: o.top || PAL.white, ink: o.ink }));
    return s;
  };

  /* plate — a slab of ground or a card: plan polygon with a little thickness */
  M.plate = function (pj, plan, thick, o) {
    o = o || {};
    var s = walls(pj, plan, 0, { base: -thick, ink: o.ink, fill: o.wall || o.fill });
    s.push(face(pj, plan, 0, { fill: o.fill || PAL.white, ink: o.ink }));
    return s;
  };

  /* field — a plate ruled into parcels by hairlines */
  M.field = function (pj, plan, thick, cuts, o) {
    o = o || {};
    var s = M.plate(pj, plan, thick, o);
    cuts.forEach(function (c) {
      s.push(line(pj(c[0], c[1], 0), pj(c[2], c[3], 0), { stroke: o.rule || PAL.cream, w: W.hair }));
    });
    return s;
  };

  /* terrain — a noise-displaced ground sample: rippled top, straight skirt */
  M.terrain = function (pj, size, hf, depth, o) {
    o = o || {};
    var n = 22, i, t, s = [];
    var far = [], nearEdge = [];
    for (i = 0; i <= n; i++) { t = size * i / n; far.push([t, 0]); }          // v = 0, u rising
    for (i = 1; i <= n; i++) { t = size * i / n; far.push([size, t]); }        // u = size
    for (i = n - 1; i >= 0; i--) { t = size * i / n; nearEdge.push([size, t]); }
    var top = [], k;
    for (i = 0; i <= n; i++) { t = size * i / n; top.push([t, 0]); }
    for (i = 1; i <= n; i++) { t = size * i / n; top.push([size, t]); }
    for (i = n - 1; i >= 0; i--) { t = size * i / n; top.push([t, size]); }
    for (i = n - 1; i >= 1; i--) { t = size * i / n; top.push([0, t]); }
    var topPts = top.map(function (p) { return pj(p[0], p[1], hf(p[0], p[1])); });

    // skirt: near boundary (v = size, then u = size) dropped to the base plane
    var nb = [];
    for (i = 0; i <= n; i++) { t = size * i / n; nb.push([t, size]); }
    for (i = n - 1; i >= 0; i--) { t = size * i / n; nb.push([size, t]); }
    var skirt = nb.map(function (p) { return pj(p[0], p[1], hf(p[0], p[1])); });
    var back = nb.slice().reverse().map(function (p) { return pj(p[0], p[1], -depth); });
    var skirtPts = skirt.concat(back);
    s.push(S(dPoly(skirtPts), skirtPts, { fill: o.fill || PAL.soil, stroke: o.ink || PAL.ink, w: W.contour }));
    s.push(S(dPoly(topPts), topPts, { fill: o.fill || PAL.soil, stroke: o.ink || PAL.ink, w: W.contour }));

    (o.contours || []).forEach(function (v) {
      var pts = [];
      for (k = 0; k <= n; k++) { t = size * k / n; pts.push(pj(t, v, hf(t, v))); }
      s.push(S(dPoly(pts, false), pts, { stroke: o.rule || PAL.cream, w: W.hair }));
    });
    return s;
  };

  /* canopy — a tree that stands in the same isometric world as the masses */
  M.canopy = function (pj, plan, o) {
    o = o || {};
    var s = M.plate(pj, plan, 0.7, { fill: PAL.soil, wall: PAL.soil, ink: o.ink });
    var mid = pj((plan[0][0] + plan[2][0]) / 2, (plan[0][1] + plan[2][1]) / 2, 0);
    var tw = o.trunk || 1.5, r = o.r || 3.9;
    var topY = mid[1] - (o.rise || 5.0);
    s.push(line([mid[0], mid[1] - 0.3], [mid[0], topY], {
      stroke: tw < TINT_MIN ? PAL.soil : PAL.ink, w: tw
    }));
    s.push(line([mid[0], topY + 2.3], [mid[0] - 1.7, topY + 0.7], { stroke: PAL.soil, w: W.hair }));
    s.push(line([mid[0], topY + 3.1], [mid[0] + 1.7, topY + 1.5], { stroke: PAL.soil, w: W.hair }));
    s.push(disc(mid[0], topY - r + 1.1, r, {
      fill: PAL.sage, stroke: o.ink || PAL.ink, w: W.contour
    }));
    return s;
  };

  /* stack — versioned definition cards */
  M.stack = function (pj, plan, count, gap, thick, o) {
    o = o || {};
    var s = [], i;
    for (i = 0; i < count; i++) {
      var lifted = pj.lift(i * gap);
      s = s.concat(walls(lifted, plan, thick, { ink: o.ink, fill: o.wall || PAL.shadeA }));
      s.push(face(lifted, plan, thick, { fill: PAL.white, ink: o.ink }));
    }
    var top = pj.lift((count - 1) * gap);
    (o.rules || []).forEach(function (c) {
      s.push(line(top(c[0], c[1], thick), top(c[2], c[3], thick), { stroke: o.rule || o.ink || PAL.ink, w: W.hair }));
    });
    return s;
  };

  /* roots — mycelial filaments. Screen-space, brand mark only. */
  M.roots = function (strands, o) {
    o = o || {};
    return strands.map(function (p) {
      return curve(p, { stroke: o.ink || PAL.ink, w: W.hair });
    });
  };

  /* ---- badge set B ---------------------------------------------------- */
  function badgeGround(accent) {
    return disc(BADGE.cx, BADGE.cy, BADGE.r, { fill: PAL.cream, stroke: accent, w: W.contour });
  }
  var B = {};

  B.gear = function (accent) {
    var teeth = 6, ro = 2.52, ri = 1.62, pts = [], i, a, half = Math.PI / teeth * 0.52;
    for (i = 0; i < teeth; i++) {
      a = i * 2 * Math.PI / teeth - Math.PI / 2;
      pts.push([BADGE.cx + ro * Math.cos(a - half), BADGE.cy + ro * Math.sin(a - half)]);
      pts.push([BADGE.cx + ro * Math.cos(a + half), BADGE.cy + ro * Math.sin(a + half)]);
      var b = a + Math.PI / teeth;
      pts.push([BADGE.cx + ri * Math.cos(b - half), BADGE.cy + ri * Math.sin(b - half)]);
      pts.push([BADGE.cx + ri * Math.cos(b + half), BADGE.cy + ri * Math.sin(b + half)]);
    }
    return [badgeGround(accent),
            S(dPoly(pts), pts, { fill: accent }),
            disc(BADGE.cx, BADGE.cy, 0.78, { fill: PAL.cream })];
  };

  B.down = function (accent) {
    return [badgeGround(accent),
            line([BADGE.cx, BADGE.cy - 2.5], [BADGE.cx, BADGE.cy + 1.45], { stroke: accent, w: W.contour }),
            S(dPoly([[BADGE.cx - 1.85, BADGE.cy - 0.45], [BADGE.cx, BADGE.cy + 1.5], [BADGE.cx + 1.85, BADGE.cy - 0.45]], false),
              [[BADGE.cx - 1.85, BADGE.cy - 0.45], [BADGE.cx + 1.85, BADGE.cy + 1.5]],
              { stroke: accent, w: W.contour })];
  };

  B.grid = function (accent) {
    var s = [badgeGround(accent)], a = 1.55, g = 0.52, o = -(a + g / 2), i, j;
    for (i = 0; i < 2; i++) for (j = 0; j < 2; j++) {
      var x = BADGE.cx + o + i * (a + g), y = BADGE.cy + o + j * (a + g);
      var pts = [[x, y], [x + a, y], [x + a, y + a], [x, y + a]];
      s.push(S(dPoly(pts), pts, { fill: accent }));   // 1.55 < TINT_MIN: tinted, never inked
    }
    return s;
  };

  /* ---- guard, emit ----------------------------------------------------- */
  function guard(shapes) {
    var bad = [], lo = SAFE.min - 1e-6, hi = SAFE.max + 1e-6;
    shapes.forEach(function (s, i) {
      (s.pts || []).forEach(function (p) {
        if (p[0] < lo || p[0] > hi || p[1] < lo || p[1] > hi) {
          bad.push('shape ' + i + ' at ' + r3(p[0]) + ',' + r3(p[1]));
        }
      });
    });
    return bad;
  }

  function inner(shapes) {
    return shapes.map(function (s) {
      var a = s.type === 'circle'
        ? '<circle cx="' + r3(s.cx) + '" cy="' + r3(s.cy) + '" r="' + r3(s.r) + '"'
        : '<path d="' + s.d + '"';
      a += ' fill="' + (s.fill || 'none') + '"';
      if (s.rule === 'evenodd') a += ' fill-rule="evenodd"';
      if (s.stroke && s.stroke !== 'none') {
        a += ' stroke="' + s.stroke + '" stroke-width="' + s.w +
             '" stroke-linecap="round" stroke-linejoin="round"';
      }
      return a + '/>';
    }).join('');
  }

  var report = [];

  function glyph(def, opt) {
    opt = opt || {};
    var accent = FAMILY[def.family];
    if (!accent) throw new Error(def.name + ': unknown family ' + def.family);
    var kit = { iso: iso, rect: rect, M: M, B: B, PAL: PAL, W: W, C: C,
                accent: accent, ink: PAL.ink, badge: BADGE, line: line, disc: disc,
                shape: S, poly: dPoly, curve: curve, tint: function (fill, width) {
                  return width < TINT_MIN ? fill : PAL.ink;
                } };
    var shapes = def.draw(kit);
    if (def.badge) {
      if (!B[def.badge]) throw new Error(def.name + ': unknown badge ' + def.badge);
      shapes = shapes.concat(B[def.badge](accent));
    }
    var bad = guard(shapes);
    report.push({ name: def.name, ok: bad.length === 0, violations: bad });
    if (bad.length && opt.strict !== false) {
      throw new Error(def.name + ': outside safe area (' + SAFE.min + '..' + SAFE.max + ') — ' + bad.join('; '));
    }
    var body = inner(shapes);
    return {
      name: def.name, component: def.component, panel: def.panel, family: def.family,
      motif: def.motif, badge: def.badge || null, accent: accent, note: def.note || '',
      shapes: shapes, inner: body, ok: bad.length === 0, violations: bad,
      svg: '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">' + body + '</svg>',
      symbol: '<symbol id="myc-' + def.name + '" viewBox="0 0 24 24">' + body + '</symbol>'
    };
  }

  var V = { C: C, PAL: PAL, FAMILY: FAMILY, W: W, SAFE: SAFE, BADGE: BADGE, TINT_MIN: TINT_MIN,
            iso: iso, rect: rect, M: M, B: B, glyph: glyph, report: report,
            defs: {}, order: [],
            def: function (o) { V.defs[o.name] = o; V.order.push(o.name); return o; },
            all: function (opt) { return V.order.map(function (n) { return glyph(V.defs[n], opt); }); },
            sprite: function (opt) {
              return '<svg xmlns="http://www.w3.org/2000/svg" style="display:none">' +
                V.all(opt).map(function (g) { return g.symbol; }).join('') + '</svg>';
            } };

  root.MYC = V;
  if (typeof module !== 'undefined' && module.exports) module.exports = V;
})(typeof window !== 'undefined' ? window : globalThis);
