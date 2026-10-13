-- Re-runnable (ACC-06): organizations onboarded before security groups get their starting groups,
-- with the person who onboarded them in Administrators, so nobody loses access when company scope
-- moves to groups. Organizations that already have them are left as they are.
select create_default_groups(o.id, o.onboarded_by)
from organizations o
where not exists (select 1 from security_groups g where g.organization_id = o.id and g.default_key = 'administrators');
