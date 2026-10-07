-- "What's your role in the business?" at sign-up: reference data, loaded by
-- post-deploy/0002_signup_roles.sql. People pick a role by id, never type it, except for
-- the one role marked is_other, which asks them to type their role (users.signup_role_other).
-- Roles are informational: access comes from security groups, never from these titles.

create table signup_role_groups (
    code        text primary key check (code ~ '^[a-z_]+$'),
    name_en     text not null,
    name_sw     text not null,
    sort_order  integer not null
);

create table signup_roles (
    id              uuid primary key,                -- fixed ids, the same in every environment
    code            text not null unique check (code ~ '^[a-z_]+$'),
    group_code      text not null references signup_role_groups (code),
    name_en         text not null,
    name_sw         text not null,
    description_en  text not null,
    description_sw  text not null,
    sort_order      integer not null,
    is_other        boolean not null default false,   -- "Other": the person types their role
    active          boolean not null default true
);

-- Only one "Other" role.
create unique index signup_roles_one_other on signup_roles (is_other) where is_other;

grant select on table signup_role_groups to pesaroll_app;
grant select on table signup_roles to pesaroll_app;
