-- What can be sent for approval (WF-03): pay runs, leave, and later kinds such as terminations.
-- Reference data (post-deploy). Organizations set up their own workflow for each kind from its
-- suggested setup; nothing is created for them. A required kind can't be sent until it is set up
-- (WF-5005), so setup and Home prompt for it. A new kind is a row here plus the feature that sends it.
--   suggested_steps  [{ "approvers": "permission" | "people" | "line_manager",
--                       "permission": "<key>", "fallback": "<key>" }], pre-filled on the setup screen
create table workflow_kinds (
    key              text primary key check (key ~ '^[a-z][a-z_]{1,39}$'),
    name_en          text not null,
    name_sw          text not null,
    required         boolean not null default false,
    suggested_steps  jsonb not null,
    sort_order       int not null default 0
);

grant select on table workflow_kinds to pesaroll_app;
