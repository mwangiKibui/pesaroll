-- A typed role is required when "Other" is chosen and not allowed otherwise.
create or replace function check_signup_role_other() returns trigger
language plpgsql security definer set search_path = public
as $$
declare
    v_is_other boolean;
begin
    if new.signup_role_id is null then
        if new.signup_role_other is not null then
            raise exception 'signup_role_other needs a signup role' using errcode = 'check_violation';
        end if;
        return new;
    end if;

    select r.is_other into v_is_other from signup_roles r where r.id = new.signup_role_id;
    if v_is_other and new.signup_role_other is null then
        raise exception 'the Other sign-up role needs a typed role' using errcode = 'check_violation';
    end if;
    if not v_is_other and new.signup_role_other is not null then
        raise exception 'a typed role is only allowed with the Other sign-up role' using errcode = 'check_violation';
    end if;
    return new;
end
$$;

revoke all on function check_signup_role_other() from public;

create trigger users_signup_role_other
    before insert or update of signup_role_id, signup_role_other on users
    for each row execute function check_signup_role_other();
