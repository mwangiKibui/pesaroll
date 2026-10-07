-- The few steps that happen before anyone is signed in, so row-level security can't
-- identify the person yet. Each function returns only what its step needs, runs as the
-- schema owner (security definer) and can be called only by the app role.

-- Sign-in: the account for an email (lowercase) or phone (+254...).
create or replace function find_account_for_signin(p_identifier text)
returns table (id uuid, password_hash text, status text, locked_until timestamptz)
language sql stable security definer set search_path = public
as $$
    select u.id, u.password_hash, u.status, u.locked_until
    from users u
    where u.email = p_identifier or u.phone = p_identifier
    limit 1
$$;

-- Sign-in: record a wrong or right password. Five wrong in a row lock the account
-- for 15 minutes; after a lock expires the count starts again. Returns locked_until.
create or replace function record_signin_result(p_user_id uuid, p_success boolean)
returns timestamptz
language plpgsql security definer set search_path = public
as $$
declare
    v_locked_until timestamptz;
begin
    if p_success then
        update users set failed_signin_count = 0, locked_until = null, updated_at = now()
        where id = p_user_id;
        return null;
    end if;

    update users set
        failed_signin_count = case when locked_until <= now() then 1 else failed_signin_count + 1 end,
        locked_until = case
            when (case when locked_until <= now() then 1 else failed_signin_count + 1 end) >= 5
                then now() + interval '15 minutes'
            when locked_until <= now() then null
            else locked_until
        end,
        updated_at = now()
    where id = p_user_id
    returning locked_until into v_locked_until;
    return v_locked_until;
end
$$;

-- Every request: the live session for a cookie token hash.
create or replace function find_session(p_token_hash bytea)
returns table (session_id uuid, user_id uuid, organization_id uuid)
language sql stable security definer set search_path = public
as $$
    select s.id, s.user_id, s.organization_id
    from sessions s
    join users u on u.id = s.user_id and u.status = 'active'
    where s.token_hash = p_token_hash
      and s.revoked_at is null
      and s.expires_at > now()
      and s.last_seen_at > now() - interval '12 hours'
$$;

-- Every request: the companies a member may see. PLACEHOLDER: for now an active member
-- sees all of the organization's companies. ACC-02 replaces this body with the combined
-- company scope of the person's security groups; callers don't change.
create or replace function member_company_ids(p_user_id uuid, p_organization_id uuid)
returns uuid[]
language sql stable security definer set search_path = public
as $$
    select coalesce(array_agg(c.id order by c.id), '{}')
    from companies c
    where c.organization_id = p_organization_id
      and exists (
          select 1 from memberships m
          where m.organization_id = p_organization_id
            and m.user_id = p_user_id
            and m.status = 'active')
$$;

revoke all on function find_account_for_signin(text) from public;
revoke all on function record_signin_result(uuid, boolean) from public;
revoke all on function find_session(bytea) from public;
revoke all on function member_company_ids(uuid, uuid) from public;
grant execute on function find_account_for_signin(text) to pesaroll_app;
grant execute on function record_signin_result(uuid, boolean) to pesaroll_app;
grant execute on function find_session(bytea) to pesaroll_app;
grant execute on function member_company_ids(uuid, uuid) to pesaroll_app;
