import assert from "node:assert/strict";
import test from "node:test";

import {
  compareClaims,
  decodeJwtPayload,
} from "./verify-github-oidc-claims.mjs";

function createUnsignedToken(payload) {
  const header = Buffer.from(JSON.stringify({ alg: "none" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.signature`;
}

test("decodes the JWT payload without exposing the complete token", () => {
  const claims = { actor_id: "58142151", sub: "repo:kbr1009/kobareo-calendar" };

  assert.deepEqual(decodeJwtPayload(createUnsignedToken(claims)), claims);
});

test("reports only mismatched expected claims", () => {
  const mismatches = compareClaims(
    { actor_id: "58142151", repository_id: "unexpected" },
    { actor_id: "58142151", repository_id: "1344909426" },
  );

  assert.deepEqual(mismatches, [
    {
      actual: "unexpected",
      expected: "1344909426",
      name: "repository_id",
    },
  ]);
});
