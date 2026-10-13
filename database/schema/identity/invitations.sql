-- ACC-03, AUTH-08: inviting a colleague "as HR, for these companies". Colleagues never sign
-- themselves up: an administrator invites them by email or SMS, and the link (single use, 7 days,
-- only its SHA-256 kept) lets them set a password, or sign in if they already have an account, and
-- join the organization in the matching group. Resending makes a new link; revoking ends it.
create table invitations (
    id               uuid primary key,
    organization_id  uuid not null references organizations (id) on delete cascade,
    first_name       text not null check (length(btrim(first_name)) between 1 and 60),
    last_name        text not null check (length(btrim(last_name)) between 1 and 60),
    email            text,                            -- stored lowercase
    phone            text,                            -- stored as +254...
    group_key        text not null check (group_key in ('administrators', 'hr', 'payroll', 'approvers')),
    all_companies    boolean not null default true,   -- else invitation_companies
    invited_by       uuid not null references users (id),
    token_hash       bytea not null unique,
    expires_at       timestamptz not null,
    sent_at          timestamptz not null default now(),
    send_count       int not null default 1 check (send_count >= 1),
    accepted_at      timestamptz,
    accepted_by      uuid references users (id),
    revoked_at       timestamptz,
    revoked_by       uuid references users (id),
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (organization_id, id),
    check (email is not null or phone is not null),
    check (accepted_at is null or revoked_at is null)
);

-- One open invitation per email and per phone in an organization.
create unique index invitations_one_open_email on invitations (organization_id, email) where email is not null and accepted_at is null and revoked_at is null;
create unique index invitations_one_open_phone on invitations (organization_id, phone) where phone is not null and accepted_at is null and revoked_at is null;

create table invitation_companies (
    organization_id  uuid not null,
    invitation_id    uuid not null,
    company_id       uuid not null,
    primary key (invitation_id, company_id),
    foreign key (organization_id, invitation_id) references invitations (organization_id, id) on delete cascade,
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade
);

alter table invitations enable row level security;
alter table invitation_companies enable row level security;

create policy invitations_tenant on invitations using (organization_id = app_org_id());
create policy invitation_companies_tenant on invitation_companies using (organization_id = app_org_id());

grant select, insert, update on table invitations to pesaroll_app;
grant select, insert on table invitation_companies to pesaroll_app;

-- An invitation by its link, for the accept page (the person isn't signed in, so row-level
-- security can't apply): who invited them, to what, and whether they already have an account.
create or replace function invitation_by_token(p_token_hash bytea)
returns table (
    id uuid, organization_name text, invited_by_name text, first_name text, last_name text, email text, phone text, group_key text,
    all_companies boolean, company_names text[], status text, expires_at timestamptz, has_account boolean)
language sql stable security definer set search_path = public
as $$
    select i.id, o.name, u.full_name, i.first_name, i.last_name, i.email, i.phone, i.group_key, i.all_companies,
           coalesce((select array_agg(c.legal_name order by c.legal_name)
                     from invitation_companies ic join companies c on c.id = ic.company_id where ic.invitation_id = i.id), '{}'),
           case when i.accepted_at is not null then 'accepted'
                when i.revoked_at is not null then 'revoked'
                when i.expires_at <= now() then 'expired'
                else 'open' end,
           i.expires_at,
           exists (select 1 from users x where (i.email is not null and x.email = i.email) or (i.phone is not null and x.phone = i.phone))
    from invitations i
    join organizations o on o.id = i.organization_id
    join users u on u.id = i.invited_by
    where i.token_hash = p_token_hash
$$;

-- The group for a role and companies, found or made: the starting group for all companies, else a
-- group with only that role for exactly those companies ("HR · Westlands Ltd"). Used by accepting an
-- invitation and by changing someone's access (Team access).
create or replace function access_group(p_organization_id uuid, p_group_key text, p_all_companies boolean, p_company_ids uuid[])
returns uuid
language plpgsql security definer set search_path = public
as $$
declare
    v_group uuid;
    v_role uuid;
    v_companies uuid[];
    v_name text;
begin
    perform create_default_groups(p_organization_id, null);
    select r.id into v_role from roles r where r.organization_id is null
      and r.key = case p_group_key when 'administrators' then 'admin' when 'approvers' then 'approver' else p_group_key end;

    if p_all_companies then
        select g.id into v_group from security_groups g where g.organization_id = p_organization_id and g.default_key = p_group_key;
        return v_group;
    end if;

    select array_agg(c.id order by c.id) into v_companies
    from companies c where c.organization_id = p_organization_id and c.id = any (p_company_ids);
    select g.id into v_group
    from security_groups g
    where g.organization_id = p_organization_id and not g.all_companies
      and (select array_agg(gr.role_id) from security_group_roles gr where gr.security_group_id = g.id) = array[v_role]
      and (select array_agg(gc.company_id order by gc.company_id) from security_group_companies gc where gc.security_group_id = g.id) = v_companies
    limit 1;
    if v_group is null then
        select left((select name from security_groups where organization_id = p_organization_id and default_key = p_group_key)
                    || ' · ' || string_agg(c.legal_name, ', ' order by c.legal_name), 110)
        into v_name from companies c where c.id = any (v_companies);
        if exists (select 1 from security_groups where organization_id = p_organization_id and lower(name) = lower(v_name)) then
            v_name := left(v_name, 100) || ' (' || left(md5(random()::text), 6) || ')';
        end if;
        v_group := uuidv7();
        insert into security_groups (id, organization_id, name, all_companies) values (v_group, p_organization_id, v_name, false);
        insert into security_group_companies (organization_id, security_group_id, company_id)
        select p_organization_id, v_group, unnest(v_companies);
        insert into security_group_roles (organization_id, security_group_id, role_id) values (p_organization_id, v_group, v_role);
    end if;
    return v_group;
end
$$;

-- Accepts an open invitation for the signed-in person (app.user_id), whose email or phone must be
-- the invitation's: they join the organization (a membership, made active again if they had left)
-- and the group for the invitation's role and companies (access_group). Returns the organization. Errors are raised as their catalogue codes.
create or replace function accept_invitation(p_token_hash bytea)
returns uuid
language plpgsql security definer set search_path = public
as $$
declare
    v_user uuid := app_user_id();
    inv invitations;
    v_membership uuid;
    v_group uuid;
begin
    if v_user is null then
        raise exception 'AUTH-1007';
    end if;
    select * into inv from invitations where token_hash = p_token_hash for update;
    if not found then
        raise exception 'AUTH-1011';
    elsif inv.accepted_at is not null then
        raise exception 'AUTH-1013';
    elsif inv.revoked_at is not null then
        raise exception 'AUTH-1014';
    elsif inv.expires_at <= now() then
        raise exception 'AUTH-1012';
    end if;
    if not exists (select 1 from users u where u.id = v_user
                   and ((inv.email is not null and u.email = inv.email) or (inv.phone is not null and u.phone = inv.phone))) then
        raise exception 'AUTH-1015';
    end if;

    insert into memberships (id, organization_id, user_id, status) values (uuidv7(), inv.organization_id, v_user, 'active')
    on conflict (organization_id, user_id) do update set status = 'active', updated_at = now()
    returning id into v_membership;

    v_group := access_group(inv.organization_id, inv.group_key, inv.all_companies,
        (select array_agg(ic.company_id) from invitation_companies ic where ic.invitation_id = inv.id));

    insert into security_group_members (id, organization_id, security_group_id, membership_id, added_by)
    select uuidv7(), inv.organization_id, v_group, v_membership, inv.invited_by
    where not exists (select 1 from security_group_members x where x.security_group_id = v_group and x.membership_id = v_membership and x.removed_at is null);

    update invitations set accepted_at = now(), accepted_by = v_user, updated_at = now() where id = inv.id;
    return inv.organization_id;
end
$$;

revoke all on function invitation_by_token(bytea) from public;
revoke all on function accept_invitation(bytea) from public;
revoke all on function access_group(uuid, text, boolean, uuid[]) from public;
grant execute on function invitation_by_token(bytea) to pesaroll_app;
grant execute on function accept_invitation(bytea) to pesaroll_app;
