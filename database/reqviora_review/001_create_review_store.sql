
-- REQVIORA PostgreSQL / Supabase
-- Run in the intended DEVELOPMENT Supabase project.

CREATE SCHEMA IF NOT EXISTS reqviora_review;

CREATE TABLE IF NOT EXISTS reqviora_review.requirements
(
    project_id UUID NOT NULL,
    requirement_id VARCHAR(100) NOT NULL,
    title VARCHAR(200) NOT NULL,
    description VARCHAR(4000) NOT NULL DEFAULT '',
    status VARCHAR(16) NOT NULL DEFAULT 'Suggested',
    revision INTEGER NOT NULL DEFAULT 1,
    evidence_json JSONB NOT NULL,
    confirmed_by UUID NULL,
    confirmed_at TIMESTAMPTZ NULL,

    CONSTRAINT pk_review_requirements
        PRIMARY KEY (project_id, requirement_id),

    CONSTRAINT ck_review_status
        CHECK (status IN (
            'Suggested', 'Confirmed',
            'Rejected', 'Deferred'
        )),

    CONSTRAINT ck_review_revision
        CHECK (revision >= 1),

    CONSTRAINT ck_review_evidence
        CHECK (
            jsonb_typeof(evidence_json) = 'array'
            AND jsonb_array_length(evidence_json) > 0
        ),

    CONSTRAINT ck_review_confirmation
        CHECK (
            (
                status = 'Confirmed'
                AND confirmed_by IS NOT NULL
                AND confirmed_at IS NOT NULL
            )
            OR
            (
                status <> 'Confirmed'
                AND confirmed_by IS NULL
                AND confirmed_at IS NULL
            )
        )
);

CREATE TABLE IF NOT EXISTS reqviora_review.events
(
    event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL,
    requirement_id VARCHAR(100) NOT NULL,
    decision VARCHAR(12) NOT NULL,
    actor_id UUID NOT NULL,
    previous_revision INTEGER NOT NULL,
    new_revision INTEGER NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT fk_review_events_requirements
        FOREIGN KEY (project_id, requirement_id)
        REFERENCES reqviora_review.requirements
            (project_id, requirement_id),

    CONSTRAINT uq_review_event_revision
        UNIQUE (project_id, requirement_id, new_revision),

    CONSTRAINT ck_review_decision
        CHECK (
            decision IN (
                'Accept', 'Edit', 'Reject', 'Defer'
            )
        ),

    CONSTRAINT ck_review_event_revision
        CHECK (new_revision = previous_revision + 1)
);

-- Keep this private schema out of client roles.
REVOKE ALL ON SCHEMA reqviora_review FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA reqviora_review FROM PUBLIC;

-- Supabase supplies these roles; standalone PostgreSQL may not.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
        REVOKE ALL ON SCHEMA reqviora_review FROM anon;
        REVOKE ALL ON ALL TABLES IN SCHEMA reqviora_review FROM anon;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
        REVOKE ALL ON SCHEMA reqviora_review FROM authenticated;
        REVOKE ALL ON ALL TABLES IN SCHEMA reqviora_review FROM authenticated;
    END IF;
END $$;
