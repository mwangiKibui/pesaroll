-- ONB-03: Kenya's banks and branches are data, with the codes from the Kenya Bankers
-- Association list; the five banks we start with each have three branches.
begin;
set local role pesaroll_app;
do $$
begin
  if (select array_agg(code order by code) from banks where country_code = 'KE' and active)
     <> array['01', '03', '07', '57', '68'] then
    raise exception 'expected KCB (01), Absa (03), NCBA (07), I&M (57) and Equity (68)';
  end if;
  if (select name from banks where country_code = 'KE' and code = '68') <> 'Equity Bank' then raise exception 'bank 68 is not Equity Bank'; end if;
  if exists (select 1 from banks b where country_code = 'KE'
             and (select count(*) from bank_branches r where r.country_code = b.country_code and r.bank_code = b.code) <> 3) then
    raise exception 'every bank should have three branches';
  end if;
  if (select name from bank_branches where country_code = 'KE' and bank_code = '01' and code = '100') <> 'Moi Avenue, Nairobi' then
    raise exception 'KCB branch 100 is not Moi Avenue, Nairobi';
  end if;
  if exists (select 1 from bank_branches where code !~ '^[0-9]{3}$') then raise exception 'branch codes are three digits'; end if;
end $$;
rollback;
