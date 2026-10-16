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
language sql stable security definer set search_path = public
as $$
    select id from roles where organization_id is null
      and key = case p_group_key when 'administrators' then 'admin' when 'approvers' then 'approver' else p_group_key end
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
    if v_role is null then
        raise exception 'ACC-8010';
    end if;
    delete from role_customizations where organization_id = app_org_id() and role_id = v_role;
    if not organization_has_administrator(app_org_id()) then
        raise exception 'ACC-8007';
    end if;
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
