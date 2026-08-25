CREATE TABLE scheduler_meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE workspaces (
    id TEXT PRIMARY KEY,
    root TEXT NOT NULL UNIQUE,
    registered_at REAL NOT NULL,
    epoch INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE tasks (
    id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    owner TEXT NOT NULL,
    summary TEXT NOT NULL,
    token_hash TEXT NOT NULL,
    state TEXT NOT NULL,
    created_at REAL NOT NULL,
    heartbeat_at REAL NOT NULL,
    expires_at REAL NOT NULL,
    finished_at REAL,
    result TEXT,
    note TEXT
);

CREATE INDEX tasks_workspace_state ON tasks(workspace_id, state);

CREATE TABLE claims (
    id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    kind TEXT NOT NULL,
    state TEXT NOT NULL,
    queue_order INTEGER NOT NULL,
    created_at REAL NOT NULL,
    granted_at REAL,
    released_at REAL
);

CREATE INDEX claims_workspace_state_order
    ON claims(workspace_id, state, queue_order);

CREATE TABLE claim_scopes (
    claim_id TEXT NOT NULL REFERENCES claims(id) ON DELETE CASCADE,
    scope_type TEXT NOT NULL,
    value TEXT NOT NULL,
    PRIMARY KEY(claim_id, scope_type, value)
);

CREATE TABLE recovery_events (
    id TEXT PRIMARY KEY,
    workspace_id TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    resolution TEXT NOT NULL,
    evidence TEXT NOT NULL,
    created_at REAL NOT NULL
);

INSERT INTO scheduler_meta(key, value) VALUES('schema_version', '1');
