import esbuild from 'esbuild';
import { mkdir, copyFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const root = __dirname;
const dist = join(root, 'dist');

const watch = process.argv.includes('--watch');

await mkdir(dist, { recursive: true });

const context = await esbuild.context({
  entryPoints: [join(root, 'src/main.ts')],
  bundle: true,
  format: 'cjs',
  platform: 'browser',
  external: ['obsidian', 'electron'],
  outfile: join(dist, 'main.js'),
  target: 'es2022',
  sourcemap: 'inline',
  logLevel: 'info',
});

await copyFile(join(root, 'manifest.json'), join(dist, 'manifest.json'));
await copyFile(join(root, 'versions.json'), join(dist, 'versions.json'));

if (watch) {
  await context.watch();
} else {
  await context.rebuild();
  await context.dispose();
}
