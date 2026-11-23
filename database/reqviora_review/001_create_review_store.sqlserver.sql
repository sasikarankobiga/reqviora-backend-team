-- REQVIORA SQL Server requirement-review store (idempotent).
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'reqviora_review')
    EXEC(N'CREATE SCHEMA reqviora_review');
GO

IF OBJECT_ID(N'reqviora_review.requirements', N'U') IS NULL
BEGIN
    CREATE TABLE reqviora_review.requirements
    (
        project_id UNIQUEIDENTIFIER NOT NULL,
        requirement_id VARCHAR(100) NOT NULL,
        title NVARCHAR(200) NOT NULL,
        description NVARCHAR(4000) NOT NULL CONSTRAINT df_review_description DEFAULT N'',
        status VARCHAR(16) NOT NULL CONSTRAINT df_review_status DEFAULT 'Suggested',
        revision INT NOT NULL CONSTRAINT df_review_revision DEFAULT 1,
        evidence_json NVARCHAR(MAX) NOT NULL,
        confirmed_by UNIQUEIDENTIFIER NULL,
        confirmed_at DATETIMEOFFSET NULL,
        CONSTRAINT pk_review_requirements PRIMARY KEY (project_id, requirement_id),
        CONSTRAINT ck_review_status CHECK (status IN ('Suggested','Confirmed','Rejected','Deferred')),
        CONSTRAINT ck_review_revision CHECK (revision >= 1),
        CONSTRAINT ck_review_evidence CHECK (ISJSON(evidence_json) = 1),
        CONSTRAINT ck_review_confirmation CHECK
        ((status = 'Confirmed' AND confirmed_by IS NOT NULL AND confirmed_at IS NOT NULL) OR
         (status <> 'Confirmed' AND confirmed_by IS NULL AND confirmed_at IS NULL))
    );
END;
GO

IF OBJECT_ID(N'reqviora_review.events', N'U') IS NULL
BEGIN
    CREATE TABLE reqviora_review.events
    (
        event_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_review_event_id DEFAULT NEWSEQUENTIALID(),
        project_id UNIQUEIDENTIFIER NOT NULL,
        requirement_id VARCHAR(100) NOT NULL,
        decision VARCHAR(12) NOT NULL,
        actor_id UNIQUEIDENTIFIER NOT NULL,
        previous_revision INT NOT NULL,
        new_revision INT NOT NULL,
        occurred_at DATETIMEOFFSET NOT NULL,
        CONSTRAINT pk_review_events PRIMARY KEY (event_id),
        CONSTRAINT fk_review_events_requirements FOREIGN KEY (project_id, requirement_id)
            REFERENCES reqviora_review.requirements(project_id, requirement_id),
        CONSTRAINT uq_review_event_revision UNIQUE (project_id, requirement_id, new_revision),
        CONSTRAINT ck_review_decision CHECK (decision IN ('Accept','Edit','Reject','Defer')),
        CONSTRAINT ck_review_event_revision CHECK (new_revision = previous_revision + 1)
    );
END;
GO
