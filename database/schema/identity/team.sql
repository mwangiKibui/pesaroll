-- ONB-11, Team access: an administrator changes a colleague's group and companies, or removes them
-- from the organization. Only someone who can manage the team for every company may (checked here
-- too, not only by the API), and the organization always keeps someone who can: the last one
-- can't be moved or removed (ACC-8007). Group memberships are closed, not deleted, so who had
-- access when stays known. Errors are raised as their catalogue codes.

-- Whether anyone active in the organization can still manage the team for every company.
create or replace function organization_has_administrator(p_organization_id uuid)
returns boolean
language sql stable security definer set search_path = public
as $$
    select exists (
        select 1 from memberships m
        where m.organization_id = p_organization_id and m.status = 'active'
          and exists (select 1 from member_grants(m.user_id, p_organization_id) g where g.permission_key = 'team.manage' and g.all_companies))
$$;

-- The membership, in the signed-in person's organization, if they may manage the team.
create or replace function team_membership_to_change(p_membership_id uuid)
returns memberships
language plpgsql stable security definer set search_path = public
as $$
declare
    m memberships;
begin
    if app_user_id() is null or app_org_id() is null then
        raise exception 'AUTH-1007';
    end if;
    if not exists (select 1 from member_grants(app_user_id(), app_org_id()) g where g.permission_key = 'team.manage' and g.all_companies) then
        raise exception 'ACC-8001';
    end if;
    select * into m from memberships where id = p_membership_id and organization_id = app_org_id() and status = 'active';
    if not found then
        raise exception 'ACC-8008';
    end if;
    return m;
end
$$;

-- Puts someone in the group for each of these roles, for these companies (access_group), and out of
-- any others: groups they keep stay as they are, new ones are added, the rest are closed.
create or replace function set_member_access(p_membership_id uuid, p_group_keys text[], p_all_companies boolean, p_company_ids uuid[])
returns void
language plpgsql security definer set search_path = public
as $$
declare
    m memberships := team_membership_to_change(p_membership_id);
    v_groups uuid[] := '{}';
    v_key text;
begin
    if coalesce(cardinality(p_group_keys), 0) = 0 then
        raise exception 'ACC-8009';
    end if;
    foreach v_key in array p_group_keys loop
        v_groups := v_groups || access_group(m.organization_id, v_key, p_all_companies, p_company_ids);
    end loop;
    update security_group_members set removed_at = now(), removed_by = app_user_id()
    where membership_id = m.id and removed_at is null and not (security_group_id = any (v_groups));
    insert into security_group_members (id, organization_id, security_group_id, membership_id, added_by)
    select uuidv7(), m.organization_id, g, m.id, app_user_id()
    from unnest(v_groups) g
    where not exists (select 1 from security_group_members x where x.security_group_id = g and x.membership_id = m.id and x.removed_at is null);
    if not organization_has_administrator(m.organization_id) then
        raise exception 'ACC-8007';
    end if;
end
$$;

-- Removes someone from the organization: their membership ends and they leave every group.
create or replace function remove_member(p_membership_id uuid)
returns void
language plpgsql security definer set search_path = public
as $$
declare
    m memberships := team_membership_to_change(p_membership_id);
begin
    update security_group_members set removed_at = now(), removed_by = app_user_id() where membership_id = m.id and removed_at is null;
    update memberships set status = 'removed', updated_at = now() where id = m.id;
    if not organization_has_administrator(m.organization_id) then
        raise exception 'ACC-8007';
    end if;
end
$$;

-- ACC-04: what a standard group (administrators, hr, payroll, approvers) may do in the signed-in
-- person's organization: exactly these permissions, for every group with that role. Only someone who
-- manages the team for every company may, and someone must still manage it afterwards (ACC-8007).
create or replace function role_for_group_key(p_group_key text)
returns uuid
language plpgsql stable security definer set search_path = public
as $$
begin
    return (select id from roles
            where (organization_id is null and p_group_key in ('administrators', 'hr', 'payroll', 'approvers')
                   and key = case p_group_key when 'administrators' then 'admin' when 'approvers' then 'approver' else p_group_key end)
               or (organization_id = app_org_id() and key = p_group_key and deleted_at is null));
end
$$;

create or replace function require_team_manager()
returns void
language plpgsql stable security definer set search_path = public
as $$
begin
    if app_user_id() is null or app_org_id() is null then
        raise exception 'AUTH-1007';
    end if;
    if not exists (select 1 from member_grants(app_user_id(), app_org_id()) g where g.permission_key = 'team.manage' and g.all_companies) then
        raise exception 'ACC-8001';
    end if;
end
$$;

create or replace function set_role_access(p_group_key text, p_permission_keys text[])
returns void
language plpgsql security definer set search_path = public
as $$
declare
    v_role uuid := role_for_group_key(p_group_key);
begin
    perform require_team_manager();
    if v_role is null or exists (select 1 from unnest(coalesce(p_permission_keys, '{}')) k where k not in (select key from permissions)) then
        raise exception 'ACC-8010';
    end if;
    insert into role_customizations (organization_id, role_id, updated_by) values (app_org_id(), v_role, app_user_id())
    on conflict (organization_id, role_id) do update set updated_by = excluded.updated_by, updated_at = now();
    delete from role_customization_permissions where organization_id = app_org_id() and role_id = v_role;
    insert into role_customization_permissions (organization_id, role_id, permission_key)
    select distinct app_org_id(), v_role, k from unnest(coalesce(p_permission_keys, '{}')) k;
    if not organization_has_administrator(app_org_id()) then
        raise exception 'ACC-8007';
    end if;
end
$$;

create or replace function reset_role_access(p_group_key text)
returns void
language plpgsql security definer set search_path = public
as $$
declare
    v_role uuid := role_for_group_key(p_group_key);
begin
    perform require_team_manager();
    -- Only the standard groups have a standard to go back to.
    if v_role is null or p_group_key not in ('administrators', 'hr', 'payroll', 'approvers') then
        raise exception 'ACC-8010';
    end if;
    delete from role_customizations where organization_id = app_org_id() and role_id = v_role;
    if not organization_has_administrator(app_org_id()) then
        raise exception 'ACC-8007';
    end if;
end
$$;

-- ACC-08: a group the organization adds: a role it owns, with its permissions (role_customizations),
-- and a group for all companies carrying it, like the standard ones. Its key comes from its name.
-- A name must be 2 to 60 characters, unused in the organization and not a standard group's (ACC-8011).
create or replace function group_name_ok(p_name text, p_except uuid)
returns boolean
language plpgsql stable security definer set search_path = public
as $$
begin
    return char_length(btrim(coalesce(p_name, ''))) between 2 and 60
       and lower(btrim(p_name)) not in ('administrator', 'administrators', 'hr', 'payroll', 'approver', 'approvers')
       and not exists (select 1 from roles r where r.organization_id = app_org_id() and r.deleted_at is null and r.id is distinct from p_except
                       and lower(r.name_en) = lower(btrim(p_name)))
       and not exists (select 1 from security_groups g where g.organization_id = app_org_id() and lower(g.name) = lower(btrim(p_name))
                       and not exists (select 1 from security_group_roles gr where gr.security_group_id = g.id and gr.role_id = p_except));
end
$$;

create or replace function add_group(p_name text, p_description text, p_permission_keys text[])
returns text
language plpgsql security definer set search_path = public
as $$
declare
    v_name text := btrim(coalesce(p_name, ''));
    v_base text;
    v_key text;
    v_role uuid := uuidv7();
    v_group uuid := uuidv7();
    n int := 0;
begin
    perform require_team_manager();
    perform create_default_groups(app_org_id(), null);
    if not group_name_ok(v_name, null) then
        raise exception 'ACC-8011';
    end if;
    if exists (select 1 from unnest(coalesce(p_permission_keys, '{}')) k where k not in (select key from permissions)) then
        raise exception 'ACC-8010';
    end if;
    v_base := left(coalesce(nullif(btrim(regexp_replace(lower(v_name), '[^a-z]+', '_', 'g'), '_'), ''), 'group'), 40);
    v_key := v_base;
    while v_key in ('administrators', 'hr', 'payroll', 'approvers', 'admin', 'approver')
          or exists (select 1 from roles where organization_id = app_org_id() and key = v_key) loop
        n := n + 1;
        v_key := v_base || '_' || translate(n::text, '0123456789', 'abcdefghij');
    end loop;
    insert into roles (id, organization_id, key, name_en, name_sw) values (v_role, app_org_id(), v_key, v_name, v_name);
    insert into role_customizations (organization_id, role_id, updated_by) values (app_org_id(), v_role, app_user_id());
    insert into role_customization_permissions (organization_id, role_id, permission_key)
    select distinct app_org_id(), v_role, k from unnest(coalesce(p_permission_keys, '{}')) k;
    insert into security_groups (id, organization_id, name, description, all_companies)
    values (v_group, app_org_id(), v_name, nullif(btrim(coalesce(p_description, '')), ''), true);
    insert into security_group_roles (organization_id, security_group_id, role_id) values (app_org_id(), v_group, v_role);
    return v_key;
end
$$;

-- Renames one of the organization's own groups; its key and people stay. Groups made for chosen
-- companies ("Branch managers · Westlands Ltd") take the new name too.
create or replace function rename_group(p_key text, p_name text, p_description text)
returns void
language plpgsql security definer set search_path = public
as $$
declare
    v_name text := btrim(coalesce(p_name, ''));
    v_role uuid;
    v_old text;
begin
    perform require_team_manager();
    select id, name_en into v_role, v_old from roles where organization_id = app_org_id() and key = p_key and deleted_at is null;
    if v_role is null then
        raise exception 'ACC-8010';
    end if;
    if not group_name_ok(v_name, v_role) then
        raise exception 'ACC-8011';
    end if;
    update roles set name_en = v_name, name_sw = v_name where id = v_role;
    update security_groups g set name = case when g.all_companies then v_name else left(v_name || substr(g.name, char_length(v_old) + 1), 120) end,
        description = case when g.all_companies then nullif(btrim(coalesce(p_description, '')), '') else g.description end, updated_at = now()
    where g.organization_id = app_org_id() and exists (select 1 from security_group_roles gr where gr.security_group_id = g.id and gr.role_id = v_role);
end
$$;

-- Deletes one of the organization's own groups once nobody is in it and no open invitation names
-- it (ACC-8012). It is kept, deleted, so who was in it stays known; the name becomes free again.
create or replace function delete_group(p_key text)
returns void
language plpgsql security definer set search_path = public
as $$
declare
    v_role uuid;
begin
    perform require_team_manager();
    select id into v_role from roles where organization_id = app_org_id() and key = p_key and deleted_at is null;
    if v_role is null then
        raise exception 'ACC-8010';
    end if;
    if exists (select 1 from security_group_roles gr
               join security_group_members gm on gm.security_group_id = gr.security_group_id and gm.removed_at is null
               join memberships m on m.id = gm.membership_id and m.status = 'active'
               where gr.role_id = v_role)
       or exists (select 1 from invitations i where i.organization_id = app_org_id() and i.accepted_at is null and i.revoked_at is null
                  and p_key = any (i.group_keys)) then
        raise exception 'ACC-8012';
    end if;
    update roles set deleted_at = now() where id = v_role;
    update security_groups g set name = left(g.name, 100) || ' (deleted ' || to_char(now(), 'YYYY-MM-DD') || ')', updated_at = now()
    where g.organization_id = app_org_id() and exists (select 1 from security_group_roles gr where gr.security_group_id = g.id and gr.role_id = v_role);
end
$$;

revoke all on function organization_has_administrator(uuid) from public;
revoke all on function team_membership_to_change(uuid) from public;
revoke all on function set_member_access(uuid, text[], boolean, uuid[]) from public;
revoke all on function remove_member(uuid) from public;
grant execute on function set_member_access(uuid, text[], boolean, uuid[]) to pesaroll_app;
grant execute on function remove_member(uuid) to pesaroll_app;
revoke all on function role_for_group_key(text) from public;
revoke all on function require_team_manager() from public;
revoke all on function set_role_access(text, text[]) from public;
revoke all on function reset_role_access(text) from public;
grant execute on function set_role_access(text, text[]) to pesaroll_app;
grant execute on function reset_role_access(text) to pesaroll_app;
revoke all on function group_name_ok(text, uuid) from public;
revoke all on function add_group(text, text, text[]) from public;
revoke all on function rename_group(text, text, text) from public;
revoke all on function delete_group(text) from public;
grant execute on function add_group(text, text, text[]) to pesaroll_app;
grant execute on function rename_group(text, text, text) to pesaroll_app;
grant execute on function delete_group(text) to pesaroll_app;
