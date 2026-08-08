const fs = require('fs');
const path = require('path');
global.window = global;
require('../design/icons/src/myc-vec.js');
require('../design/icons/src/myc-vec-set.js');
const root = path.resolve(__dirname, '..');
const glyphs = global.MYC.all();
for (const glyph of glyphs) {
  fs.writeFileSync(path.join(root, 'design', 'icons', 'svg', glyph.name + '.svg'), glyph.svg);
}
fs.writeFileSync(path.join(root, 'design', 'icons', 'Mycelium_Icons_Vector.svg'), global.MYC.sprite());
process.stdout.write(glyphs.length + ' SVG icons generated\n');
