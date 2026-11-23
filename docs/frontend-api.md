# REQVIORA frontend API guide

All project-content endpoints require `Authorization: Bearer <access-token>`. Owners have full access, Editors may read and edit workflow content, and Viewers have read-only access. Approval, export creation, metadata updates, and member administration remain owner-only. JSON errors use ASP.NET `ProblemDetails`. A missing or invalid token returns `401`; unauthorized project access returns `403`; missing content returns `404`; and stale draft tokens return `409`.

## Authentication and projects

| Method | Route | Body | Success |
| --- | --- | --- | --- |
| POST | `/api/auth/register` | `{ "email", "password", "displayName" }` | `201 AuthResponseDto` |
| POST | `/api/auth/login` | `{ "email", "password" }` | `200 AuthResponseDto` |
| POST | `/api/auth/refresh` | `{ "refreshToken" }` | `200 AuthResponseDto` |
| POST | `/api/auth/logout` | `{ "refreshToken" }` | `204` |
| POST | `/api/account-security/email-verification/request` | none; Bearer required | `202` or `204` if already verified |
| POST | `/api/account-security/email-verification/confirm` | `{ "token" }` | `204` |
| POST | `/api/account-security/password/forgot` | `{ "email" }` | `202` for known and unknown accounts |
| POST | `/api/account-security/password/reset` | `{ "token", "newPassword" }` | `204` |
| POST | `/api/account-security/email-change/request` | `{ "newEmail" }`; Bearer required | `202` |
| POST | `/api/account-security/email-change/confirm` | `{ "token" }` | `204` |
| GET | `/api/users/me` | none | `200 UserProfileDto` |
| GET/POST | `/api/projects` | `CreateProjectDto` for POST | project list / `201` |
| GET/PUT | `/api/projects/{projectId}` | `UpdateProjectDto` for PUT | project / `200` |
| GET | `/api/projects/{projectId}/members` | none | owner/member roster |
| PUT | `/api/projects/{projectId}/members/{userId}` | `{ "role": "Viewer" | "Editor" }` | add/update member |
| DELETE | `/api/projects/{projectId}/members/{userId}` | none | `204` |
| GET | `/api/projects/{projectId}/overview` | none | project workflow counts and current token |
| GET | `/api/workspaces` | none | owned workspaces |
| GET | `/api/workspaces/{workspaceId}` | none | owned workspace |

Registration passwords must contain 8–128 characters.

Project create and update use the same nine frontend fields: `name`, `description`, `industry`, `targetAudience`, `projectType`, `primaryPlatform`, `technologyPreferences`, `constraints`, and `integrations`. The owner identity is always taken from the access token and is never writable from the request body.

## Draft and Blueprint workflow

Create the first draft without a token. Every successful write returns a new `concurrencyToken`; send that value as `expectedConcurrencyToken` on the next write.

```json
{
  "draft": { "projectNotes": "Draft content" },
  "expectedConcurrencyToken": null
}
```

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/projects/{projectId}/drafts/current` | Resume the current draft |
| PUT | `/api/projects/{projectId}/drafts/current` | Save generic draft JSON |
| GET | `/api/projects/{projectId}/drafts/versions` | Immutable version history |
| POST | `/api/projects/{projectId}/drafts/start-after-approval` | Start a child draft from the approved revision |
| GET | `/api/projects/{projectId}/blueprints/current` | Current Blueprint v1 document |
| PUT | `/api/projects/{projectId}/blueprints/current` | Save draft JSON and a Blueprint v1 document |
| GET | `/api/projects/{projectId}/blueprints/versions` | Blueprint version history |
| POST | `/api/projects/{projectId}/blueprints/validate` | Validate the current version using its draft token |
| GET | `/api/projects/{projectId}/blueprints/validations/{validationId}` | Validation result and findings |
| GET | `/api/projects/{projectId}/blueprints/approved` | Approved immutable Blueprints |
| GET | `/api/projects/{projectId}/blueprints/approved/{revisionId}` | One approved Blueprint |

A stale or missing token on an existing draft returns `409`. Approval locks that draft. Call `start-after-approval` with its latest token before making further changes.

## Requirements and generated graph

These endpoints return only persisted Blueprint content; they do not invoke an AI provider or fabricate missing data.

| Method | Route |
| --- | --- |
| GET | `/api/projects/{projectId}/requirements` |
| GET | `/api/projects/{projectId}/requirements/{requirementId}` |
| POST | `/api/projects/{projectId}/requirements/{requirementId}/review` |
| GET | `/api/projects/{projectId}/features` |
| GET | `/api/projects/{projectId}/features/{featureId}` |
| GET | `/api/projects/{projectId}/screens` |
| GET | `/api/projects/{projectId}/screens/{screenId}` |
| GET | `/api/projects/{projectId}/navigation` |

Review decisions are `accept`, `edit`, `reject`, or `defer` and require `expectedRevision`. `edit` also requires `editedTitle` and `editedDescription`.

## Approval and Second Brain export

```json
{
  "expectedConcurrencyToken": "current-draft-token",
  "validationRunId": "passing-validation-id"
}
```

| Method | Route | Purpose |
| --- | --- | --- |
| POST | `/api/projects/{projectId}/approvals` | Approve the current validated version |
| GET | `/api/projects/{projectId}/approvals` | Approval history |
| GET | `/api/projects/{projectId}/approvals/{revisionId}` | Approval status |
| POST | `/api/projects/{projectId}/exports/from-revision/{revisionId}` | Create or reuse a verified ZIP |
| GET | `/api/projects/{projectId}/exports` | Export metadata |
| GET | `/api/projects/{projectId}/exports/{exportId}` | One export's metadata |
| GET | `/api/projects/{projectId}/exports/{exportId}/download` | Authorized ZIP download |

An approval requires a passing validation run for the exact current draft version. Export reads verify the manifest and SHA-256 digest before returning content.

## Configuration

- `ConnectionStrings__ReqvioraDatabase`
- `Jwt__Key` (at least 32 characters)
- `Jwt__Issuer`
- `Jwt__Audience`
- `Jwt__AccessTokenMinutes`
- `Jwt__RefreshTokenDays`
- `AccountLinks__BaseUrl` (HTTPS frontend origin for account-action links)
- `Cors__AllowedOrigins__0` and subsequent indexes for browser clients

Swagger is available at `/swagger` in Development. Apply the EF migrations and the matching PostgreSQL `.sql` or SQL Server `.sqlserver.sql` Requirement Review schema before using all endpoints.

## Input intelligence and feature-gated MCP V1 scope

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/projects/{projectId}/input-intelligence/requirements/extract` | Generate evidence-bound requirement candidates; body must include `consentGranted: true` and human review is still mandatory |
| POST | `/api/projects/{projectId}/input-intelligence/voice/transcribe` | Transcribe consented Tamil or English multipart audio |
| GET | `/api/mcp/v1/catalog` | Feature-gated read-only tools, resources and prompt catalog |
| POST | `/api/mcp/v1/tools/call` | Invoke an allow-listed owner-scoped read-only tool |
| GET | `/api/mcp/v1/prompts/{name}` | Read an approved V1 prompt template |

Text extraction and voice transcription validate explicit consent and project authorization before checking provider availability. Input-intelligence and AI-job endpoints return `503` while `AI__Enabled=false`. Keep this default until G0-07 approves a provider, then configure the approved endpoint, models, and secret-backed key before enabling it. Set `Mcp__Enabled=true` only after a separate MCP scope approval. The V1 surface contains no mutating MCP tool.
