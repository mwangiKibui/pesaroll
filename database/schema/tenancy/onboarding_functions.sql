-- ONB-02, the organization step. One function so the organization, the person's membership and
-- the switch of their session to it happen together or not at all. It acts for the signed-in
-- person (app.user_id), never an id the caller passes, and only for people who signed up
-- themselves (users.signed_up_via = 'self'). One onboarded organization per person for now: stops duplicates from double-clicks and junk organizations (relax here when needed).
-- Errors are raised as their catalogue codes; the API turns them into responses.
create or replace function create_organization(p_name text, p_session_id uuid)
returns uuid
language plpgsql security definer set search_path = public
as $$
declare
    v_user uuid := app_user_id();
    v_name text := btrim(coalesce(p_name, ''));
    v_org  uuid := uuidv7();
begin
    if v_user is null then
        raise exception 'AUTH-1007';
    end if;
    if (select u.signed_up_via from users u where u.id = v_user) is distinct from 'self' then
        raise exception 'ONB-3504';
    end if;
    if char_length(v_name) not between 2 and 120 then
        raise exception 'ONB-3501';
    end if;

    -- Two clicks or two tabs at once: the second waits here, then sees the first.
    perform pg_advisory_xact_lock(hashtextextended('create_organization:' || v_user::text, 0));
    if exists (select 1 from organizations o where o.onboarded_by = v_user) then
        raise exception 'ONB-3502';
    end if;

    insert into organizations (id, name, onboarded_by) values (v_org, v_name, v_user);
    insert into memberships (id, organization_id, user_id, status) values (uuidv7(), v_org, v_user, 'active');
    -- Its starting groups, with the person who onboarded it in Administrators (ACC-06).
    perform create_default_groups(v_org, v_user);

    update sessions s set organization_id = v_org
    where s.id = p_session_id and s.user_id = v_user and s.revoked_at is null;
    if not found then
        raise exception 'AUTH-1010';
    end if;

    return v_org;
end
$$;

-- Whether the signed-in person has onboarded an organization. The app offers the organization
-- step only to people who signed up themselves and haven't (row-level security hides other
-- organizations from them, hence a function).
create or replace function has_onboarded_organization()
returns boolean
language sql stable security definer set search_path = public
as $$
    select exists (select 1 from organizations o where o.onboarded_by = app_user_id())
$$;

revoke all on function has_onboarded_organization() from public;
grant execute on function has_onboarded_organization() to pesaroll_app;
revoke all on function create_organization(text, uuid) from public;
grant execute on function create_organization(text, uuid) to pesaroll_app;
