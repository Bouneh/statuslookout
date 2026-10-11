import { build } from 'esbuild';
import { mkdir, copyFile, readFile, writeFile } from 'node:fs/promises';
const target = 'com.bouneh.statuslookout.sdPlugin';
await mkdir(`${target}/bin`, {recursive: true});
await build({entryPoints: ['src/plugin.ts'], bundle: true, platform: 'node', format: 'esm', target: 'node24',
  outfile: `${target}/bin/plugin.mjs`, banner: {js: "import { createRequire } from 'node:module'; const require = createRequire(import.meta.url);"},
  sourcemap: false, minify: true });
await copyFile('../../LICENSE', `${target}/LICENSE`);
const elgato = await readFile('ELGATO-LICENSE.txt', 'utf8');
const ws = await readFile('node_modules/ws/LICENSE', 'utf8');
await writeFile(`${target}/THIRD-PARTY-NOTICES.txt`, `StatusLookout Stream Deck runtime dependencies\n\n@elgato/streamdeck, @elgato/utils, @elgato/schemas\nSource: https://github.com/elgatosf/streamdeck\n\n${elgato}\n\nws\nSource: https://github.com/websockets/ws\n\n${ws}`);
console.log('Built StatusLookout Stream Deck plugin.');
