-- Records which pre-deploy scripts have run (see db.sh). Keep in step with db.sh.
create table deploy_scripts (
    name        text primary key,
    applied_at  timestamptz not null default now()
);
