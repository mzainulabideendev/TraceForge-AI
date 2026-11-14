import { cp, mkdir, readdir, rm, stat } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Copies the production Angular build into the API's wwwroot so a single deployment serves both the
 * console and /api from one origin. Cookies are SameSite=Strict, so same-origin is required.
 */

const here = dirname(fileURLToPath(import.meta.url));
const frontendRoot = resolve(here, '..');
const source = join(frontendRoot, 'dist', 'traceforge-web', 'browser');
const target = resolve(frontendRoot, '..', 'backend', 'TraceForge.Api', 'wwwroot');

async function exists(path) {
  try {
    await stat(path);
    return true;
  } catch {
    return false;
  }
}

if (!(await exists(source))) {
  console.error(`Build output not found at ${source}. Run "npm run build" first.`);
  process.exit(1);
}

await rm(target, { recursive: true, force: true });
await mkdir(target, { recursive: true });
await cp(source, target, { recursive: true });

const entries = await readdir(target);
if (!entries.includes('index.html')) {
  console.error(`Copy finished but ${target} has no index.html.`);
  process.exit(1);
}

console.log(`Copied ${entries.length} entries from ${source}`);
console.log(`to ${target}`);
