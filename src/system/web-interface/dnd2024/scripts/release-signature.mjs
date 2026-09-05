import { createPublicKey, sign, verify } from 'node:crypto';
import assert from 'node:assert/strict';
import { sha256 } from './create-release-manifest.mjs';

export function canonicalJson(value) {
  if (Array.isArray(value)) return '[' + value.map(canonicalJson).join(',') + ']';
  if (value !== null && typeof value === 'object') return '{' + Object.keys(value).sort()
    .map(key => JSON.stringify(key) + ':' + canonicalJson(value[key])).join(',') + '}';
  return JSON.stringify(value);
}

export function signManifest(manifest, privateKey) {
  const { signature: ignored, ...payload } = manifest;
  const publicKey = createPublicKey(privateKey);
  assert.equal(publicKey.asymmetricKeyType, 'ed25519');
  return { ...payload, signature: { algorithm: 'Ed25519',
    keyFingerprint: sha256(publicKey.export({ type: 'spki', format: 'der' })),
    value: sign(null, Buffer.from(canonicalJson(payload)), privateKey).toString('base64') } };
}

export function verifyManifest(manifest, trustedPublicKey) {
  assert.ok(trustedPublicKey, 'An independently pinned release public key is required');
  const { signature, ...payload } = manifest;
  assert.equal(signature?.algorithm, 'Ed25519', 'Unsigned manifests cannot prove a release');
  const key = createPublicKey(trustedPublicKey);
  assert.equal(key.asymmetricKeyType, 'ed25519');
  assert.equal(signature.keyFingerprint, sha256(key.export({ type: 'spki', format: 'der' })));
  assert.ok(verify(null, Buffer.from(canonicalJson(payload)), key, Buffer.from(signature.value, 'base64')),
    'Release manifest signature is invalid');
  return payload;
}
