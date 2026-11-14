#!/usr/bin/env node
/**
 * Non-mutating P1 npm audit classification for CI.
 * No tokens, passwords or source code are printed. No npm audit fix is run.
 */
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const frontend = resolve(dirname(fileURLToPath(import.meta.url)), '../frontend/marketplace-ui');
for (const [scope, extraArgs] of [['all', []], ['runtime', ['--omit=dev']]]) {
  const result = spawnSync('npm', ['audit', '--json', ...extraArgs], {
    cwd: frontend, encoding: 'utf8', timeout: 120000, maxBuffer: 8 * 1024 * 1024,
    env: { ...process.env, CI: 'true' },
  });
  if (result.error || (result.status !== 0 && result.status !== 1)) {
    throw new Error('npm audit failed to execute for scope ' + scope +
      ': status=' + result.status + '; error=' + (result.error?.code ?? 'none'));
  }
  let audit;
  try {
    audit = JSON.parse(result.stdout);
  } catch {
    throw new Error('npm audit did not return valid JSON for scope ' + scope);
  }
  if (!audit.metadata?.vulnerabilities || !audit.vulnerabilities) {
    throw new Error('Incomplete npm audit report for scope ' + scope);
  }
  const packages = Object.entries(audit.vulnerabilities)
    .map(([name, v]) => ({
      name,
      severity: v.severity,
      direct: !!v.isDirect,
      range: v.range,
      advisories: (v.via ?? []).filter(x => typeof x === 'object')
        .map(x => ({ title: x.title, url: x.url, range: x.range })),
      fixAvailable: v.fixAvailable
    }))
    .sort((a, b) => a.name.localeCompare(b.name));
  console.log('P1_NPM_AUDIT_' + scope.toUpperCase() + '=' + JSON.stringify({
    counts: audit.metadata.vulnerabilities, packages
  }));
}
