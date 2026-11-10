-- GameNest: one-time promotion of the FIRST administrator.
--
-- Usage (see README, "First administrator"):
--   1. Register the future admin through the API and confirm the email address.
--   2. Replace the placeholder email below with that user's email address.
--   3. Run:  psql -h <host> -U <user> -d gamenest -v ON_ERROR_STOP=1 -f scripts/bootstrap-first-admin.sql
--
-- Safety rules enforced by the script (any violation aborts and rolls back):
--   * the placeholder email must have been replaced;
--   * no user may already have the Admin role (this is a first-admin procedure only);
--   * exactly one user must have the given email address;
--   * that user's email address must be confirmed.
-- Further admins must be granted by an existing admin through a reviewed process, not by this script.

BEGIN;

-- Prevent two operators from bootstrapping concurrently.
LOCK TABLE "Users" IN SHARE ROW EXCLUSIVE MODE;

DO $$
DECLARE
    placeholder   CONSTANT text := 'replace-with-admin-email@example.com';
    target_email  text := lower(trim('replace-with-admin-email@example.com'));
    admin_count   integer;
    match_count   integer;
    is_confirmed  boolean;
BEGIN
    IF target_email = placeholder THEN
        RAISE EXCEPTION 'Replace the placeholder email in this script before running it.';
    END IF;

    SELECT count(*) INTO admin_count FROM "Users" WHERE "Role" = 'Admin';
    IF admin_count > 0 THEN
        RAISE EXCEPTION 'An administrator already exists (% found). This script only creates the first administrator.', admin_count;
    END IF;

    SELECT count(*) INTO match_count FROM "Users" WHERE lower("Email") = target_email;
    IF match_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one user with this email address, found %.', match_count;
    END IF;

    SELECT "EmailConfirmed" INTO is_confirmed FROM "Users" WHERE lower("Email") = target_email;
    IF NOT is_confirmed THEN
        RAISE EXCEPTION 'The user''s email address is not confirmed. Confirm it before promoting the user.';
    END IF;

    UPDATE "Users" SET "Role" = 'Admin' WHERE lower("Email") = target_email;
    RAISE NOTICE 'User promoted to Admin. Existing JWTs keep the old role until they expire; the user must log in again.';
END
$$;

COMMIT;
