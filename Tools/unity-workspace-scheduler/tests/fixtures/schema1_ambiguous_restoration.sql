INSERT INTO workspaces(id, root, registered_at, epoch)
VALUES('__WORKSPACE_ID__', '__WORKSPACE_ROOT__', 1000, 7);

INSERT INTO tasks(
    id, workspace_id, owner, summary, token_hash, state,
    created_at, heartbeat_at, expires_at
) VALUES
    ('owner-task', '__WORKSPACE_ID__', 'owner', 'restoring path owner',
     'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
     'active', 1000, 1100, 2900),
    ('urgent-task', '__WORKSPACE_ID__', 'urgent-two', 'second urgent freeze',
     'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
     'active', 1001, 1100, 2900),
    ('normal-task', '__WORKSPACE_ID__', 'normal', 'later normal freeze',
     'cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc',
     'active', 1002, 1100, 2900);

INSERT INTO claims(
    id, workspace_id, task_id, kind, state, queue_order, created_at, granted_at
) VALUES
    ('ambiguous-owner-claim', '__WORKSPACE_ID__', 'owner-task', 'normal', 'queued', 1,
     1000, NULL),
    ('active-urgent-freeze', '__WORKSPACE_ID__', 'urgent-task', 'freeze', 'active', 3,
     1001, 1050),
    ('queued-normal-freeze', '__WORKSPACE_ID__', 'normal-task', 'freeze', 'queued', 4,
     1002, NULL);

INSERT INTO claim_scopes(claim_id, scope_type, value)
VALUES('ambiguous-owner-claim', 'write', 'assets/hero.prefab');

INSERT INTO claim_scopes(claim_id, scope_type, value)
VALUES('active-urgent-freeze', 'priority', 'urgent');
