#!/usr/bin/env node
'use strict';

// Durable verifier for the throwaway, self-contained prototype. It deliberately
// evaluates only the pure state-machine and scenario declarations: no DOM,
// browser, network, credential, or mapping APIs are available here.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const prototypePath = path.join(__dirname, 'v0.3-endpoint-switching-logic-prototype.html');
const html = fs.readFileSync(prototypePath, 'utf8').replace(/\r\n/g, '\n');

function between(start, end) {
  const from = html.indexOf(start);
  const to = html.indexOf(end, from);
  assert.notEqual(from, -1, `Missing prototype marker: ${start}`);
  assert.notEqual(to, -1, `Missing prototype marker: ${end}`);
  return html.slice(from, to);
}

const machineSource = between('const EndpointMachine =', '\n\nconst scenarios =')
  .replace('const EndpointMachine =', 'globalThis.EndpointMachine =');
const scenariosSource = between('const scenarios =', '\n\nlet state =')
  .replace('const scenarios =', 'globalThis.scenarios =');
const context = { console };
vm.createContext(context);
vm.runInContext(machineSource, context, { filename: prototypePath });
vm.runInContext(scenariosSource, context, { filename: prototypePath });

const { EndpointMachine, scenarios } = context;
assert.ok(EndpointMachine && scenarios, 'Expected the extracted pure machine and guided scenarios.');
assert.equal(scenarios.length, 7, 'Expected exactly seven guided scenarios.');

function apply(state, action) {
  return EndpointMachine.reducer(state, action);
}

function runScenario(scenario) {
  let state = scenario.setup();
  for (const [, action] of scenario.steps) state = apply(state, action);
  return state;
}

const results = new Map(scenarios.map(scenario => [scenario.title, runScenario(scenario)]));
const rootFor = path => results.get(path).mapping?.root;

// Every guided walkthrough executes from its declared reset state.
assert.equal(results.get('LAN success').outcome, 'Ready');
assert.equal(results.get('LAN success').mapping.path, 'lan');
assert.equal(results.get('LAN success').authenticationAttempts, 1);
assert.equal(results.get('Tailscale success').outcome, 'Ready');
assert.equal(results.get('Tailscale success').mapping.path, 'tailscale');
assert.match(rootFor('Tailscale success'), /\.ts\.net\\Balls$/);
assert.equal(results.get('Selected path down').outcome, 'Ready');
assert.equal(results.get('Selected path down').mapping.path, 'tailscale');
assert.equal(results.get('Selected path down').authenticationAttempts, 1);
assert.equal(results.get('Name drift').outcome, 'Ready');
assert.equal(results.get('Name drift').mapping.root, '\\\\harbor-pc.tail7c2.ts.net\\Balls');
assert.equal(results.get('Mapping replacement').outcome, 'Ready');
assert.equal(results.get('Mapping replacement').mapping.path, 'tailscale');
assert.equal(results.get('Mapping replacement').authenticationAttempts, 1);
assert.equal(results.get('Credential collision').outcome, 'Ready');
assert.equal(results.get('Credential collision').authenticationAttempts, 1);
assert.equal(results.get('Illegal IP fallback').outcome, 'Refused');
assert.equal(results.get('Illegal IP fallback').authenticationAttempts, 0);

// One attempt means the selected failing path is not retried on the alternate.
let down = EndpointMachine.initial({ selectedPath:'lan', selectedRoot:'\\\\HARBOR-PC\\Balls' });
down = apply(down, { type:'MARK_PATH_DOWN', path:'lan' });
const downBeforeVerify = down.authenticationAttempts;
down = apply(down, { type:'VERIFY_SELECTED' });
assert.equal(down.authenticationAttempts, downBeforeVerify, 'Unavailable selected path must not authenticate.');
assert.equal(down.selectedPath, 'lan', 'Unavailable LAN must not select Tailscale automatically.');
assert.match(down.message, /No automatic alternate attempt/);

const noFallbackBefore = EndpointMachine.initial({ selectedPath:'lan', selectedRoot:'\\\\HARBOR-PC\\Balls' });
const noFallbackAfter = apply(noFallbackBefore, { type:'AUTO_FALLBACK' });
assert.equal(noFallbackAfter.outcome, 'Refused');
assert.equal(noFallbackAfter.selectedPath, 'lan');
assert.equal(noFallbackAfter.authenticationAttempts, 0);

// A credential collision stops before it consumes a password attempt.
let collision = apply(EndpointMachine.initial(), { type:'SELECT_PATH', path:'lan' });
collision = apply(collision, { type:'SET_CREDENTIAL_COLLISION' });
const collisionBeforeVerify = collision.authenticationAttempts;
collision = apply(collision, { type:'VERIFY_SELECTED' });
assert.equal(collision.outcome, 'Action required');
assert.equal(collision.authenticationAttempts, collisionBeforeVerify);
assert.match(collision.message, /before an SMB attempt/);

// IP is transport observation only; it cannot create or change a connection identity.
let ip = EndpointMachine.initial({ selectedPath:'lan', selectedRoot:'\\\\HARBOR-PC\\Balls' });
ip = apply(ip, { type:'OPEN_IP_DIAGNOSTIC' });
assert.match(ip.ipDiagnostic, /transport|TCP/i);
const selectedBeforeIpRefusal = ip.selectedRoot;
ip = apply(ip, { type:'SELECT_IP_AS_ENDPOINT' });
assert.equal(ip.outcome, 'Refused');
assert.equal(ip.selectedRoot, selectedBeforeIpRefusal);
assert.equal(ip.mapping, null);
assert.equal(ip.savedCredentialTarget, null);
assert.equal(ip.authenticationAttempts, 0);

// Switching is a previewed replacement, never a hidden remap.
const noPreview = apply(EndpointMachine.initial(), { type:'CONFIRM_SWITCH' });
assert.equal(noPreview.outcome, 'Refused');
assert.match(noPreview.message, /preview/);

// Every unsupported or out-of-order transition fails closed without mutation.
const invalidSelection = apply(EndpointMachine.initial(), { type:'SELECT_PATH', path:'ip' });
assert.equal(invalidSelection.outcome, 'Refused');
assert.equal(invalidSelection.selectedPath, null);
const mappingWithoutVerification = apply(EndpointMachine.initial({ selectedPath:'lan', selectedRoot:'\\\\HARBOR-PC\\Balls' }), { type:'ADD_MAPPING' });
assert.equal(mappingWithoutVerification.outcome, 'Refused');
assert.equal(mappingWithoutVerification.mapping, null);
const previewWithoutMapping = apply(EndpointMachine.initial({ selectedPath:'tailscale', selectedRoot:'\\\\harbor-pc.tail7c2.ts.net\\Balls' }), { type:'PREVIEW_SWITCH' });
assert.equal(previewWithoutMapping.outcome, 'Refused');
const unknownTransition = apply(EndpointMachine.initial(), { type:'DISCOVER_OR_PAIR' });
assert.equal(unknownTransition.outcome, 'Refused');

// Regression: drift/reobserve/select/verify cannot replace a mapping without an imported update.
const driftMappedRoot = '\\\\harbor-pc.tail7c2.ts.net\\Balls';
let driftBypass = EndpointMachine.initial({
  selectedPath:'tailscale', selectedRoot:driftMappedRoot, verifiedPath:'tailscale',
  mapping:{ root:driftMappedRoot, path:'tailscale', drive:'B:', persistent:true },
  savedCredentialTarget:EndpointMachine.credentialTargets.tailscale, outcome:'Ready'
});
driftBypass = apply(driftBypass, { type:'DRIFT', path:'tailscale', name:'harbor-pc-1.tail7c2.ts.net', root:'\\\\harbor-pc-1.tail7c2.ts.net\\Balls' });
driftBypass = apply(driftBypass, { type:'REOBSERVE' });
driftBypass = apply(driftBypass, { type:'SELECT_PATH', path:'tailscale' });
driftBypass = apply(driftBypass, { type:'VERIFY_SELECTED' });
driftBypass = apply(driftBypass, { type:'ADD_MAPPING' });
assert.equal(driftBypass.outcome, 'Refused');
assert.equal(driftBypass.mapping.root, driftMappedRoot);
assert.equal(driftBypass.savedCredentialTarget, EndpointMachine.credentialTargets.tailscale);

// Endpoint updates bind one endpoint to the existing host/grant/revision and import no credential target.
const mappedClient = EndpointMachine.initial({
  selectedPath:'lan', selectedRoot:'\\\\HARBOR-PC\\Balls', verifiedPath:'lan',
  mapping:{ root:'\\\\HARBOR-PC\\Balls', path:'lan', drive:'B:', persistent:true },
  savedCredentialTarget:EndpointMachine.credentialTargets.lan, outcome:'Ready'
});
const untransferredAlternate = apply(mappedClient, { type:'SELECT_PATH', path:'tailscale' });
assert.equal(untransferredAlternate.outcome, 'Refused');
assert.equal(untransferredAlternate.selectedPath, 'lan');
assert.equal(untransferredAlternate.selectedRoot, mappedClient.selectedRoot);
assert.equal(untransferredAlternate.authenticationAttempts, 0);
const validUpdate = {
  schemaVersion:1, productHostId:mappedClient.hostId, grantId:mappedClient.grantId,
  credentialRevision:mappedClient.credentialRevision, shareName:mappedClient.share,
  endpoint:{ kind:'tailscale', root:'\\\\harbor-pc.tail7c2.ts.net\\Balls' }, generatedAt:'2026-08-14T16:00:00Z'
};
const importRefusals = [
  { ...validUpdate, productHostId:'host_wrong' },
  { ...validUpdate, grantId:'grant_wrong' },
  { ...validUpdate, credentialRevision:validUpdate.credentialRevision - 1 },
  { ...validUpdate, password:'must-not-transfer' },
  { ...validUpdate, alternateEndpoint:{ kind:'lan', root:'\\\\HARBOR-PC\\Balls' } },
  { ...validUpdate, discoveryHint:'scan-local-network' },
  { ...validUpdate, endpoint:{ ...validUpdate.endpoint, credentialTarget:'must-be-proven-separately' } },
  { ...validUpdate, endpoint:{ kind:'tailscale', root:'\\\\100.64.0.1\\Balls' } },
  { ...validUpdate, endpoint:{ kind:'lan', root:'\\\\HARBOR-PC\\Balls' } },
  { ...validUpdate, generatedAt:'not-a-timestamp' },
  { ...validUpdate, generatedAt:'2026-99-99T99:99:99Z' }
];
for (const bundle of importRefusals) {
  const refused = EndpointMachine.importEndpointUpdate(mappedClient, bundle);
  assert.equal(refused.outcome, 'Refused');
  assert.equal(refused.pendingEndpointUpdate, null);
  assert.equal(JSON.stringify(refused.mapping), JSON.stringify(mappedClient.mapping));
  assert.equal(refused.savedCredentialTarget, mappedClient.savedCredentialTarget);
  assert.equal(refused.authenticationAttempts, 0);
}

let imported = EndpointMachine.importEndpointUpdate(mappedClient, validUpdate);
assert.equal(imported.outcome, 'Action required');
assert.equal(imported.pendingEndpointUpdate.endpoint.root, validUpdate.endpoint.root);
assert.equal(imported.pendingEndpointUpdate.provenCredentialTarget, null);
assert.equal(JSON.stringify(imported.mapping), JSON.stringify(mappedClient.mapping));
assert.equal(imported.savedCredentialTarget, mappedClient.savedCredentialTarget);
assert.equal(imported.authenticationAttempts, 0);
assert.equal(EndpointMachine.previewImportedSwitch(imported).outcome, 'Refused');
assert.equal(EndpointMachine.proveProviderTarget(imported, validUpdate.endpoint.root).outcome, 'Refused');
const invalidProviderTargets = [
  ['arbitrary provider string', 'provider-target:anything'],
  ['old LAN server target', EndpointMachine.credentialTargets.lan]
];
const providerTargetAcceptanceFailures = invalidProviderTargets
  .map(([label, target]) => [label, EndpointMachine.proveProviderTarget(imported, target).outcome])
  .filter(([, outcome]) => outcome !== 'Refused');
assert.deepEqual(providerTargetAcceptanceFailures, []);
assert.equal(EndpointMachine.credentialTargets.lan, '\\\\HARBOR-PC');
assert.equal(EndpointMachine.credentialTargets.tailscale, '\\\\harbor-pc.tail7c2.ts.net');
imported = EndpointMachine.proveProviderTarget(imported, EndpointMachine.credentialTargets.tailscale);
assert.equal(imported.outcome, 'Action required');
const invalidReimport = EndpointMachine.importEndpointUpdate(imported, { ...validUpdate, credentialRevision:validUpdate.credentialRevision - 1 });
assert.equal(invalidReimport.outcome, 'Refused');
assert.equal(invalidReimport.pendingEndpointUpdate, null);
assert.equal(invalidReimport.switchPreview, null);
assert.equal(invalidReimport.authenticationAttempts, 0);
const staleRevision = EndpointMachine.previewImportedSwitch({ ...imported, credentialRevision:imported.credentialRevision + 1 });
assert.equal(staleRevision.outcome, 'Refused');
assert.equal(staleRevision.authenticationAttempts, 0);
assert.equal(JSON.stringify(staleRevision.mapping), JSON.stringify(mappedClient.mapping));
let importedPreview = EndpointMachine.previewImportedSwitch(imported);
assert.equal(importedPreview.outcome, 'Action required');
assert.equal(importedPreview.switchPreview.from, mappedClient.mapping.root);
assert.equal(importedPreview.switchPreview.to, validUpdate.endpoint.root);
assert.equal(importedPreview.switchPreview.fromCredentialTarget, mappedClient.savedCredentialTarget);
assert.equal(importedPreview.switchPreview.toCredentialTarget, EndpointMachine.credentialTargets.tailscale);
assert.notEqual(importedPreview.switchPreview.toCredentialTarget, importedPreview.switchPreview.to);
const attemptsBeforeSwitch = importedPreview.authenticationAttempts;
const collisionAfterPreview = EndpointMachine.confirmImportedSwitch({ ...importedPreview, credentialCollision:'named conflict' });
assert.equal(collisionAfterPreview.outcome, 'Action required');
assert.equal(collisionAfterPreview.authenticationAttempts, attemptsBeforeSwitch);
assert.equal(JSON.stringify(collisionAfterPreview.mapping), JSON.stringify(mappedClient.mapping));
const importedSwitched = EndpointMachine.confirmImportedSwitch(importedPreview);
assert.equal(importedSwitched.outcome, 'Ready');
assert.equal(importedSwitched.authenticationAttempts, attemptsBeforeSwitch + 1);
assert.equal(importedSwitched.mapping.root, validUpdate.endpoint.root);
assert.equal(importedSwitched.savedCredentialTarget, EndpointMachine.credentialTargets.tailscale);
assert.equal(importedSwitched.pendingEndpointUpdate, null);

// The evidence count names actual owner-interactive controls, never headings or prose.
const expectedOwnerControls = [
  'Choose trusted local network', 'Choose private Tailscale', 'Mark LAN unavailable',
  'Mark Tailscale unavailable', 'Re-observe names', 'Verify selected path once',
  'Preview explicit switch', 'Confirm switch &amp; verify once', 'Record persistent mapping',
  'Introduce credential collision', 'Run IP transport observation',
  'Attempt IP as SMB endpoint', 'Attempt automatic fallback'
];
const actualOwnerControls = [...html.matchAll(/<button\b[^>]*\bdata-owner-control\b[^>]*>([^<]+)<\/button>/g)]
  .map(match => match[1].trim());
assert.deepEqual(actualOwnerControls, expectedOwnerControls);
for (const title of scenarios.map(scenario => scenario.title)) assert.ok(html.includes(title), `Missing visible scenario tab: ${title}`);

console.log(`PASS: ${scenarios.length} guided scenarios, endpoint invariants, and ${actualOwnerControls.length} owner-interactive controls verified.`);
console.log(`PASS: ${importRefusals.length} endpoint-update binding/refusal cases, separate provider-target proof, and CRLF/LF extraction verified.`);
