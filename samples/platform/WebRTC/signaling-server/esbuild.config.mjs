import { build } from 'esbuild';

await build({
  entryPoints: ['src/index.ts'],
  bundle: true,
  platform: 'node',
  target: 'node22',
  outfile: 'build/signaling-server.cjs',
  format: 'cjs',
  sourcemap: false,
  minify: true,
  // Resolve the local shared package
  external: [],
});

console.log('✅ Bundle created: build/signaling-server.cjs');
