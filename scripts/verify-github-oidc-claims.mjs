import { appendFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

const visibleClaimNames = [
  "actor_id",
  "environment",
  "ref",
  "repository_id",
  "repository_owner_id",
  "sub",
  "workflow_ref",
];

export function decodeJwtPayload(token) {
  const segments = token.split(".");

  if (segments.length !== 3) {
    throw new Error("GitHub OIDC response was not a JWT.");
  }

  return JSON.parse(Buffer.from(segments[1], "base64url").toString("utf8"));
}

export function compareClaims(claims, expectedClaims) {
  return Object.entries(expectedClaims)
    .filter(([, expected]) => expected !== undefined)
    .filter(([name, expected]) => String(claims[name] ?? "") !== expected)
    .map(([name, expected]) => ({
      actual: String(claims[name] ?? "<missing>"),
      expected,
      name,
    }));
}

function requireEnvironment(name) {
  const value = process.env[name];

  if (!value) {
    throw new Error(`Required environment variable ${name} is missing.`);
  }

  return value;
}

async function requestOidcClaims() {
  const requestUrl = new URL(requireEnvironment("ACTIONS_ID_TOKEN_REQUEST_URL"));
  requestUrl.searchParams.set("audience", requireEnvironment("OIDC_AUDIENCE"));

  const response = await fetch(requestUrl, {
    headers: {
      Authorization: `Bearer ${requireEnvironment("ACTIONS_ID_TOKEN_REQUEST_TOKEN")}`,
    },
  });

  if (!response.ok) {
    throw new Error(`GitHub OIDC request failed with HTTP ${response.status}.`);
  }

  const body = await response.json();

  if (typeof body.value !== "string") {
    throw new Error("GitHub OIDC response did not contain a token.");
  }

  return decodeJwtPayload(body.value);
}

async function writeSummary(claims, mismatches, diagnosticOnly) {
  const summaryPath = requireEnvironment("GITHUB_STEP_SUMMARY");
  const visibleClaims = Object.fromEntries(
    visibleClaimNames.map((name) => [name, String(claims[name] ?? "<missing>")]),
  );
  const lines = [
    "### GitHub OIDC claim verification",
    "",
    `- mode: ${diagnosticOnly ? "diagnostic only; Google authentication skipped" : "enforced before Google authentication"}`,
    `- result: ${mismatches.length === 0 ? "all expected claims match" : "claim mismatch detected"}`,
    "",
    "| Claim | Value |",
    "| --- | --- |",
    ...Object.entries(visibleClaims).map(([name, value]) => `| ${name} | \`${value}\` |`),
  ];

  if (mismatches.length > 0) {
    lines.push(
      "",
      "Mismatches:",
      ...mismatches.map(
        ({ actual, expected, name }) =>
          `- ${name}: expected \`${expected}\`, received \`${actual}\``,
      ),
    );
  }

  await appendFile(summaryPath, `${lines.join("\n")}\n`, "utf8");
}

async function main() {
  const diagnosticOnly = process.env.DIAGNOSTIC_ONLY === "true";
  const claims = await requestOidcClaims();
  const expectedClaims = {
    actor_id: requireEnvironment("EXPECTED_ACTOR_ID"),
    repository_id: requireEnvironment("EXPECTED_REPOSITORY_ID"),
    repository_owner_id: requireEnvironment("EXPECTED_OWNER_ID"),
    sub: requireEnvironment("EXPECTED_SUB"),
    workflow_ref: requireEnvironment("EXPECTED_WORKFLOW_REF"),
  };
  const mismatches = compareClaims(claims, expectedClaims);

  await writeSummary(claims, mismatches, diagnosticOnly);

  if (mismatches.length > 0 && !diagnosticOnly) {
    throw new Error("GitHub OIDC claims do not satisfy the expected WIF boundary.");
  }

  if (mismatches.length > 0) {
    console.log("OIDC diagnostic completed with claim mismatches; Google authentication was skipped.");
    return;
  }

  console.log("OIDC diagnostic completed; all expected claims match.");
}

const entryPoint = process.argv[1] ? pathToFileURL(process.argv[1]).href : undefined;

if (import.meta.url === entryPoint) {
  await main();
}
