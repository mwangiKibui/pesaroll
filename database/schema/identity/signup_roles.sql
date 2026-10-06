-- The options for "What's your role in the business?" at sign-up. Reference data, loaded by
-- post-deploy/0002_signup_roles.sql; people pick one, never type it.
create table signup_roles (
    code            text primary key check (code ~ '^[a-z_]+$'),
    name_en         text not null,
    name_sw         text not null,
    description_en  text not null,
    description_sw  text not null,
    sort_order      integer not null,
    active          boolean not null default true
);

grant select on table signup_roles to pesaroll_app;
