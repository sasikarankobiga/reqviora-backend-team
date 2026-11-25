#!/usr/bin/env bash
set -euo pipefail

: "${PGHOST:?PGHOST is required}"
: "${PGPORT:?PGPORT is required}"
: "${PGUSER:?PGUSER is required}"
: "${PGPASSWORD:?PGPASSWORD is required}"
: "${PGDATABASE:?PGDATABASE is required}"

expected_major="${POSTGRES_CLIENT_MAJOR:-16}"
declare -A postgres_tools
for tool in pg_dump pg_restore psql createdb dropdb; do
  tool_path="$(command -v "${tool}")"
  tool_version="$(${tool_path} --version)"
  if [[ ! "${tool_version}" =~ PostgreSQL\)\ ${expected_major}\. ]]; then
    echo "${tool} must be PostgreSQL ${expected_major}.x, found: ${tool_version}" >&2
    exit 1
  fi
  postgres_tools["${tool}"]="${tool_path}"
  echo "${tool}: ${tool_path} (${tool_version})"
done

server_version="$(${postgres_tools[psql]} --tuples-only --no-align --command="SHOW server_version;")"
if [[ "${server_version%%.*}" != "${expected_major}" ]]; then
  echo "PostgreSQL client/server major mismatch: client ${expected_major}, server ${server_version}." >&2
  exit 1
fi
echo "PostgreSQL server: ${server_version}"

source_fixture="$(${postgres_tools[psql]} --tuples-only --no-align \
  --command="SELECT to_regclass('reqviora_review.requirements') IS NOT NULL;")"
if [[ "${source_fixture}" != "t" ]]; then
  echo "Expected reqviora_review.requirements CI fixture table is missing." >&2
  exit 1
fi
source_tables="$(${postgres_tools[psql]} --tuples-only --no-align \
  --command="SELECT count(*) FROM pg_catalog.pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema');")"

restore_database="reqviora_restore_drill_${GITHUB_RUN_ID:-local}_${RANDOM}"
work_directory="$(mktemp -d "${RUNNER_TEMP:-/tmp}/reqviora-backup.XXXXXX")"
dump_file="${work_directory}/reqviora.dump"

cleanup() {
  "${postgres_tools[dropdb]}" --if-exists --force "${restore_database}" >/dev/null 2>&1 || true
  rm -f -- "${dump_file}" "${dump_file}.sha256"
  rmdir -- "${work_directory}" 2>/dev/null || true
}
trap cleanup EXIT

"${postgres_tools[pg_dump]}" --format=custom --no-owner --no-privileges --file="${dump_file}" "${PGDATABASE}"
sha256sum "${dump_file}" > "${dump_file}.sha256"
sha256sum --check "${dump_file}.sha256"

"${postgres_tools[createdb]}" "${restore_database}"
"${postgres_tools[pg_restore]}" --exit-on-error --no-owner --no-privileges \
  --dbname="${restore_database}" "${dump_file}"

restored_tables="$(${postgres_tools[psql]} --dbname="${restore_database}" --tuples-only --no-align \
  --command="SELECT count(*) FROM pg_catalog.pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema');")"
restored_fixture="$(${postgres_tools[psql]} --dbname="${restore_database}" --tuples-only --no-align \
  --command="SELECT to_regclass('reqviora_review.requirements') IS NOT NULL;")"
if [[ "${restored_tables}" != "${source_tables}" || "${restored_fixture}" != "t" ]]; then
  echo "Restore drill schema verification failed (source=${source_tables}, restored=${restored_tables})." >&2
  exit 1
fi

echo "Backup checksum verified and ${restored_tables} table(s) restored into isolated database ${restore_database}."
