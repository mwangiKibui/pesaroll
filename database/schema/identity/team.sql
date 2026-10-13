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

-- Moves someone to the group for this role and companies (access_group), out of any others.
create or replace function set_member_access(p_membership_id uuid, p_group_key text, p_all_companies boolean, p_company_ids uuid[])
returns void
language plpgsql security definer set search_path = public
as $$
declare
    m memberships := team_membership_to_change(p_membership_id);
    v_group uuid;
begin
    v_group := access_group(m.organization_id, p_group_key, p_all_companies, p_company_ids);
    update security_group_members set removed_at = now(), removed_by = app_user_id()
    where membership_id = m.id and removed_at is null and security_group_id <> v_group;
    insert into security_group_members (id, organization_id, security_group_id, membership_id, added_by)
    select uuidv7(), m.organization_id, v_group, m.id, app_user_id()
    where not exists (select 1 from security_group_members x where x.security_group_id = v_group and x.membership_id = m.id and x.removed_at is null);
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

revoke all on function organization_has_administrator(uuid) from public;
revoke all on function team_membership_to_change(uuid) from public;
revoke all on function set_member_access(uuid, text, boolean, uuid[]) from public;
revoke all on function remove_member(uuid) from public;
grant execute on function set_member_access(uuid, text, boolean, uuid[]) to pesaroll_app;
grant execute on function remove_member(uuid) to pesaroll_app;
