# REQVIORA - Workflow and Review Integration

## Source

This branch is based on the new team's develop baseline.

Original backend:
sasikarankobiga/reqviora-backend

Original develop snapshot:
b73122630649be7af098fa4a97bfacda8e18bab2

The workflow and review changes were transferred from the
independently tested local integrated backend source.

## Workflow and Test Scope

- Kobiga workflow API and workflow policy
- Requirement review and Blueprint synchronization
- Approval validation and safe regeneration
- Workflow API integration tests
- Blueprint synchronization tests
- Approval, decision mapping and synchronization policy tests

## Shared Integration Dependencies

The changes also include database entities, EF Core migrations,
DbContext configuration, persistence stores and draft services.

These shared changes may include work from other REQVIORA
contributors. Their presence in this branch does not mean
that the branch committer originally authored every file.

Original Git history and separate backups are retained
for reviewing historical contributions and ownership.

## Verification

- .NET SDK 10.0.400
- Solution build passed
- Application tests passed
- Infrastructure tests passed
- API integration tests: 72 passed
- Blueprint contract tests: 51 passed
- Review API contract tests: 38 passed
- Remaining standalone contract suites passed

Database-specific smoke tests are not included in these
results.

## Contribution Note

The baseline and integration commits document importing
and integrating team code. Individual authorship should
be established from the original history and actual work.