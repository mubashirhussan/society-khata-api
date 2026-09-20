-- ONE-TIME production migration: converts every uuid primary/foreign key in the old
-- Society Khata schema to a sequential integer, preserving every relationship exactly.
--
-- Run this BEFORE deploying the int-based backend code, against the CURRENT (uuid) database.
-- It is wrapped in a single transaction: if anything fails, everything rolls back and the
-- database is left exactly as it was — safe to fix and re-run. Rehearsed against a realistic
-- replica (multi-tenant, soft-deleted rows, cancelled dues, overflowing installments, the
-- circular Payment<->InstallmentDue FK, self-referencing SourcePaymentId) with a byte-for-byte
-- before/after data diff and a live run of the real (int-based) app against the result — see
-- the deployment runbook for the exact steps, and take a full pg_dump backup first regardless.
--
-- Usage (from the VPS, against the production Postgres container):
--   docker compose exec -T db psql -U postgres -d society_khata -f - < backend/migrations/2026-09-18_uuid_to_int_ids.sql
--
-- NOTE: this build of production has no "PaymentInstallmentAllocations" table yet (confirmed
-- via information_schema before running this), so that table is intentionally not touched here.
-- The new backend code creates it fresh, with the correct integer columns, on its first startup.
BEGIN;

-- ===================== PHASE 1: assign a new sequential int id per table =====================
ALTER TABLE "Tenants" ADD COLUMN "NewId" integer;
WITH ordered AS (SELECT "Id", row_number() OVER (ORDER BY "CreatedAt", "Id") AS rn FROM "Tenants")
UPDATE "Tenants" t SET "NewId" = o.rn FROM ordered o WHERE o."Id" = t."Id";

ALTER TABLE "TenantRoles" ADD COLUMN "NewId" integer;
WITH ordered AS (SELECT "Id", row_number() OVER (ORDER BY "TenantId", "Name") AS rn FROM "TenantRoles")
UPDATE "TenantRoles" t SET "NewId" = o.rn FROM ordered o WHERE o."Id" = t."Id";

ALTER TABLE "Users" ADD COLUMN "NewId" integer;
WITH ordered AS (SELECT "Id", row_number() OVER (ORDER BY "CreatedAt", "Id") AS rn FROM "Users")
UPDATE "Users" u SET "NewId" = o.rn FROM ordered o WHERE o."Id" = u."Id";

ALTER TABLE "Clients" ADD COLUMN "NewId" integer;
WITH ordered AS (SELECT "Id", row_number() OVER (ORDER BY "CreatedAt", "Id") AS rn FROM "Clients")
UPDATE "Clients" c SET "NewId" = o.rn FROM ordered o WHERE o."Id" = c."Id";

ALTER TABLE "Properties" ADD COLUMN "NewId" integer;
WITH ordered AS (SELECT "Id", row_number() OVER (ORDER BY "CreatedAt", "Id") AS rn FROM "Properties")
UPDATE "Properties" p SET "NewId" = o.rn FROM ordered o WHERE o."Id" = p."Id";

ALTER TABLE "Payments" ADD COLUMN "NewId" integer;
WITH ordered AS (SELECT "Id", row_number() OVER (ORDER BY "CreatedAt", "Id") AS rn FROM "Payments")
UPDATE "Payments" p SET "NewId" = o.rn FROM ordered o WHERE o."Id" = p."Id";

ALTER TABLE "InstallmentDues" ADD COLUMN "NewId" integer;
WITH ordered AS (SELECT "Id", row_number() OVER (ORDER BY "CreatedAt", "Id") AS rn FROM "InstallmentDues")
UPDATE "InstallmentDues" d SET "NewId" = o.rn FROM ordered o WHERE o."Id" = d."Id";

ALTER TABLE "Expenses" ADD COLUMN "NewId" integer;
WITH ordered AS (SELECT "Id", row_number() OVER (ORDER BY "CreatedAt", "Id") AS rn FROM "Expenses")
UPDATE "Expenses" e SET "NewId" = o.rn FROM ordered o WHERE o."Id" = e."Id";

-- ===================== PHASE 2: remap every foreign key column via the mappings above =====================
ALTER TABLE "TenantRoles" ADD COLUMN "NewTenantId" integer;
UPDATE "TenantRoles" c SET "NewTenantId" = t."NewId" FROM "Tenants" t WHERE t."Id" = c."TenantId";

ALTER TABLE "TenantRolePermissions" ADD COLUMN "NewTenantRoleId" integer;
UPDATE "TenantRolePermissions" rp SET "NewTenantRoleId" = r."NewId" FROM "TenantRoles" r WHERE r."Id" = rp."TenantRoleId";

ALTER TABLE "Users" ADD COLUMN "NewTenantId" integer, ADD COLUMN "NewTenantRoleId" integer;
UPDATE "Users" u SET "NewTenantId" = t."NewId" FROM "Tenants" t WHERE t."Id" = u."TenantId";
UPDATE "Users" u SET "NewTenantRoleId" = r."NewId" FROM "TenantRoles" r WHERE r."Id" = u."TenantRoleId";

ALTER TABLE "Clients" ADD COLUMN "NewTenantId" integer;
UPDATE "Clients" c SET "NewTenantId" = t."NewId" FROM "Tenants" t WHERE t."Id" = c."TenantId";

ALTER TABLE "Properties" ADD COLUMN "NewTenantId" integer, ADD COLUMN "NewClientId" integer;
UPDATE "Properties" p SET "NewTenantId" = t."NewId" FROM "Tenants" t WHERE t."Id" = p."TenantId";
UPDATE "Properties" p SET "NewClientId" = c."NewId" FROM "Clients" c WHERE c."Id" = p."ClientId";

ALTER TABLE "Payments"
    ADD COLUMN "NewTenantId" integer,
    ADD COLUMN "NewClientId" integer,
    ADD COLUMN "NewPropertyId" integer,
    ADD COLUMN "NewInstallmentDueId" integer,
    ADD COLUMN "NewSourcePaymentId" integer;
UPDATE "Payments" p SET "NewTenantId" = t."NewId" FROM "Tenants" t WHERE t."Id" = p."TenantId";
UPDATE "Payments" p SET "NewClientId" = c."NewId" FROM "Clients" c WHERE c."Id" = p."ClientId";
UPDATE "Payments" p SET "NewPropertyId" = pr."NewId" FROM "Properties" pr WHERE pr."Id" = p."PropertyId";
UPDATE "Payments" p SET "NewInstallmentDueId" = d."NewId" FROM "InstallmentDues" d WHERE d."Id" = p."InstallmentDueId";
UPDATE "Payments" p SET "NewSourcePaymentId" = s."NewId" FROM "Payments" s WHERE s."Id" = p."SourcePaymentId";

ALTER TABLE "InstallmentDues"
    ADD COLUMN "NewTenantId" integer,
    ADD COLUMN "NewClientId" integer,
    ADD COLUMN "NewPropertyId" integer,
    ADD COLUMN "NewPaymentId" integer;
UPDATE "InstallmentDues" d SET "NewTenantId" = t."NewId" FROM "Tenants" t WHERE t."Id" = d."TenantId";
UPDATE "InstallmentDues" d SET "NewClientId" = c."NewId" FROM "Clients" c WHERE c."Id" = d."ClientId";
UPDATE "InstallmentDues" d SET "NewPropertyId" = pr."NewId" FROM "Properties" pr WHERE pr."Id" = d."PropertyId";
UPDATE "InstallmentDues" d SET "NewPaymentId" = p."NewId" FROM "Payments" p WHERE p."Id" = d."PaymentId";

ALTER TABLE "Expenses" ADD COLUMN "NewTenantId" integer;
UPDATE "Expenses" e SET "NewTenantId" = t."NewId" FROM "Tenants" t WHERE t."Id" = e."TenantId";

-- Sanity: every FK that had a non-null old uuid value must have resolved to a new int id.
DO $$
DECLARE bad_count integer;
BEGIN
    SELECT
        (SELECT count(*) FROM "TenantRoles" WHERE "TenantId" IS NOT NULL AND "NewTenantId" IS NULL) +
        (SELECT count(*) FROM "TenantRolePermissions" WHERE "TenantRoleId" IS NOT NULL AND "NewTenantRoleId" IS NULL) +
        (SELECT count(*) FROM "Users" WHERE ("TenantId" IS NOT NULL AND "NewTenantId" IS NULL) OR ("TenantRoleId" IS NOT NULL AND "NewTenantRoleId" IS NULL)) +
        (SELECT count(*) FROM "Clients" WHERE "TenantId" IS NOT NULL AND "NewTenantId" IS NULL) +
        (SELECT count(*) FROM "Properties" WHERE ("TenantId" IS NOT NULL AND "NewTenantId" IS NULL) OR ("ClientId" IS NOT NULL AND "NewClientId" IS NULL)) +
        (SELECT count(*) FROM "Payments" WHERE ("TenantId" IS NOT NULL AND "NewTenantId" IS NULL) OR ("ClientId" IS NOT NULL AND "NewClientId" IS NULL) OR ("PropertyId" IS NOT NULL AND "NewPropertyId" IS NULL) OR ("InstallmentDueId" IS NOT NULL AND "NewInstallmentDueId" IS NULL) OR ("SourcePaymentId" IS NOT NULL AND "NewSourcePaymentId" IS NULL)) +
        (SELECT count(*) FROM "InstallmentDues" WHERE ("TenantId" IS NOT NULL AND "NewTenantId" IS NULL) OR ("ClientId" IS NOT NULL AND "NewClientId" IS NULL) OR ("PropertyId" IS NOT NULL AND "NewPropertyId" IS NULL) OR ("PaymentId" IS NOT NULL AND "NewPaymentId" IS NULL)) +
        (SELECT count(*) FROM "Expenses" WHERE "TenantId" IS NOT NULL AND "NewTenantId" IS NULL)
    INTO bad_count;
    IF bad_count > 0 THEN
        RAISE EXCEPTION 'Migration aborted: % foreign key value(s) failed to remap. Nothing was changed.', bad_count;
    END IF;
END $$;

-- ===================== PHASE 3: swap old uuid columns for the new int ones =====================
-- Drop every FK constraint FIRST (across all tables) — a table's PK can't be dropped while
-- any other table's FK still depends on it, so no PK drop may happen until this pass is done.
ALTER TABLE "TenantRolePermissions" DROP CONSTRAINT IF EXISTS "FK_TenantRolePermissions_TenantRoles_TenantRoleId";
ALTER TABLE "TenantRoles" DROP CONSTRAINT IF EXISTS "FK_TenantRoles_Tenants_TenantId";
ALTER TABLE "Users" DROP CONSTRAINT IF EXISTS "FK_Users_Tenants_TenantId";
ALTER TABLE "Users" DROP CONSTRAINT IF EXISTS "FK_Users_TenantRoles_TenantRoleId";
ALTER TABLE "Clients" DROP CONSTRAINT IF EXISTS "FK_Clients_Tenants_TenantId";
ALTER TABLE "Properties" DROP CONSTRAINT IF EXISTS "FK_Properties_Tenants_TenantId";
ALTER TABLE "Properties" DROP CONSTRAINT IF EXISTS "FK_Properties_Clients_ClientId";
ALTER TABLE "Payments" DROP CONSTRAINT IF EXISTS "FK_Payments_Tenants_TenantId";
ALTER TABLE "Payments" DROP CONSTRAINT IF EXISTS "FK_Payments_Clients_ClientId";
ALTER TABLE "Payments" DROP CONSTRAINT IF EXISTS "FK_Payments_Properties_PropertyId";
ALTER TABLE "Payments" DROP CONSTRAINT IF EXISTS "FK_Payments_InstallmentDues_InstallmentDueId";
ALTER TABLE "InstallmentDues" DROP CONSTRAINT IF EXISTS "FK_InstallmentDues_Clients_ClientId";
ALTER TABLE "InstallmentDues" DROP CONSTRAINT IF EXISTS "FK_InstallmentDues_Properties_PropertyId";
ALTER TABLE "InstallmentDues" DROP CONSTRAINT IF EXISTS "FK_InstallmentDues_Payments_PaymentId";
ALTER TABLE "Expenses" DROP CONSTRAINT IF EXISTS "FK_Expenses_Tenants_TenantId";

-- Now drop the plain (non-PK) indexes. IF EXISTS guards against minor naming drift between
-- environments — the sanity check above already guarantees the data itself remapped correctly.
DROP INDEX IF EXISTS "IX_TenantRoles_TenantId_Name";
DROP INDEX IF EXISTS "IX_Users_TenantId_Email";
DROP INDEX IF EXISTS "IX_Clients_TenantId";
DROP INDEX IF EXISTS "IX_Properties_TenantId";
DROP INDEX IF EXISTS "IX_Properties_ClientId";
DROP INDEX IF EXISTS "IX_Payments_TenantId";
DROP INDEX IF EXISTS "IX_Payments_ClientId";
DROP INDEX IF EXISTS "IX_Payments_PropertyId";
DROP INDEX IF EXISTS "IX_Payments_InstallmentDueId";
DROP INDEX IF EXISTS "IX_InstallmentDues_TenantId_Status_DueDate";
DROP INDEX IF EXISTS "IX_InstallmentDues_ClientId";
DROP INDEX IF EXISTS "IX_InstallmentDues_PropertyId";
DROP INDEX IF EXISTS "IX_InstallmentDues_PaymentId";
DROP INDEX IF EXISTS "IX_Expenses_TenantId";

-- Now every PK is safe to drop — no FK anywhere still references it.
ALTER TABLE "TenantRolePermissions" DROP CONSTRAINT IF EXISTS "PK_TenantRolePermissions";
ALTER TABLE "TenantRoles" DROP CONSTRAINT IF EXISTS "PK_TenantRoles";
ALTER TABLE "Users" DROP CONSTRAINT IF EXISTS "PK_Users";
ALTER TABLE "Clients" DROP CONSTRAINT IF EXISTS "PK_Clients";
ALTER TABLE "Properties" DROP CONSTRAINT IF EXISTS "PK_Properties";
ALTER TABLE "Payments" DROP CONSTRAINT IF EXISTS "PK_Payments";
ALTER TABLE "InstallmentDues" DROP CONSTRAINT IF EXISTS "PK_InstallmentDues";
ALTER TABLE "Expenses" DROP CONSTRAINT IF EXISTS "PK_Expenses";
ALTER TABLE "Tenants" DROP CONSTRAINT IF EXISTS "PK_Tenants";

-- Drop the old uuid columns and rename the new int ones into their place.
ALTER TABLE "Tenants" DROP COLUMN "Id";
ALTER TABLE "Tenants" RENAME COLUMN "NewId" TO "Id";

ALTER TABLE "TenantRoles" DROP COLUMN "Id", DROP COLUMN "TenantId";
ALTER TABLE "TenantRoles" RENAME COLUMN "NewId" TO "Id";
ALTER TABLE "TenantRoles" RENAME COLUMN "NewTenantId" TO "TenantId";

ALTER TABLE "TenantRolePermissions" DROP COLUMN "TenantRoleId";
ALTER TABLE "TenantRolePermissions" RENAME COLUMN "NewTenantRoleId" TO "TenantRoleId";

ALTER TABLE "Users" DROP COLUMN "Id", DROP COLUMN "TenantId", DROP COLUMN "TenantRoleId";
ALTER TABLE "Users" RENAME COLUMN "NewId" TO "Id";
ALTER TABLE "Users" RENAME COLUMN "NewTenantId" TO "TenantId";
ALTER TABLE "Users" RENAME COLUMN "NewTenantRoleId" TO "TenantRoleId";

ALTER TABLE "Clients" DROP COLUMN "Id", DROP COLUMN "TenantId";
ALTER TABLE "Clients" RENAME COLUMN "NewId" TO "Id";
ALTER TABLE "Clients" RENAME COLUMN "NewTenantId" TO "TenantId";

ALTER TABLE "Properties" DROP COLUMN "Id", DROP COLUMN "TenantId", DROP COLUMN "ClientId";
ALTER TABLE "Properties" RENAME COLUMN "NewId" TO "Id";
ALTER TABLE "Properties" RENAME COLUMN "NewTenantId" TO "TenantId";
ALTER TABLE "Properties" RENAME COLUMN "NewClientId" TO "ClientId";

ALTER TABLE "Payments" DROP COLUMN "Id", DROP COLUMN "TenantId", DROP COLUMN "ClientId", DROP COLUMN "PropertyId", DROP COLUMN "InstallmentDueId", DROP COLUMN "SourcePaymentId";
ALTER TABLE "Payments" RENAME COLUMN "NewId" TO "Id";
ALTER TABLE "Payments" RENAME COLUMN "NewTenantId" TO "TenantId";
ALTER TABLE "Payments" RENAME COLUMN "NewClientId" TO "ClientId";
ALTER TABLE "Payments" RENAME COLUMN "NewPropertyId" TO "PropertyId";
ALTER TABLE "Payments" RENAME COLUMN "NewInstallmentDueId" TO "InstallmentDueId";
ALTER TABLE "Payments" RENAME COLUMN "NewSourcePaymentId" TO "SourcePaymentId";

ALTER TABLE "InstallmentDues" DROP COLUMN "Id", DROP COLUMN "TenantId", DROP COLUMN "ClientId", DROP COLUMN "PropertyId", DROP COLUMN "PaymentId";
ALTER TABLE "InstallmentDues" RENAME COLUMN "NewId" TO "Id";
ALTER TABLE "InstallmentDues" RENAME COLUMN "NewTenantId" TO "TenantId";
ALTER TABLE "InstallmentDues" RENAME COLUMN "NewClientId" TO "ClientId";
ALTER TABLE "InstallmentDues" RENAME COLUMN "NewPropertyId" TO "PropertyId";
ALTER TABLE "InstallmentDues" RENAME COLUMN "NewPaymentId" TO "PaymentId";

ALTER TABLE "Expenses" DROP COLUMN "Id", DROP COLUMN "TenantId";
ALTER TABLE "Expenses" RENAME COLUMN "NewId" TO "Id";
ALTER TABLE "Expenses" RENAME COLUMN "NewTenantId" TO "TenantId";

-- ===================== PHASE 4: re-add constraints/indexes to match the new int-based model =====================
ALTER TABLE "Tenants" ALTER COLUMN "Id" SET NOT NULL;
ALTER TABLE "Tenants" ADD CONSTRAINT "PK_Tenants" PRIMARY KEY ("Id");

ALTER TABLE "TenantRoles" ALTER COLUMN "Id" SET NOT NULL, ALTER COLUMN "TenantId" SET NOT NULL;
ALTER TABLE "TenantRoles" ADD CONSTRAINT "PK_TenantRoles" PRIMARY KEY ("Id");
ALTER TABLE "TenantRoles" ADD CONSTRAINT "FK_TenantRoles_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantRoles_TenantId_Name" ON "TenantRoles" ("TenantId", "Name");

ALTER TABLE "TenantRolePermissions" ALTER COLUMN "TenantRoleId" SET NOT NULL;
ALTER TABLE "TenantRolePermissions" ADD CONSTRAINT "PK_TenantRolePermissions" PRIMARY KEY ("TenantRoleId", "PermissionKey");
ALTER TABLE "TenantRolePermissions" ADD CONSTRAINT "FK_TenantRolePermissions_TenantRoles_TenantRoleId" FOREIGN KEY ("TenantRoleId") REFERENCES "TenantRoles" ("Id") ON DELETE CASCADE;
CREATE INDEX IF NOT EXISTS "IX_TenantRolePermissions_PermissionKey" ON "TenantRolePermissions" ("PermissionKey");

ALTER TABLE "Users" ALTER COLUMN "Id" SET NOT NULL, ALTER COLUMN "TenantId" SET NOT NULL, ALTER COLUMN "TenantRoleId" SET NOT NULL;
ALTER TABLE "Users" ADD CONSTRAINT "PK_Users" PRIMARY KEY ("Id");
ALTER TABLE "Users" ADD CONSTRAINT "FK_Users_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE;
ALTER TABLE "Users" ADD CONSTRAINT "FK_Users_TenantRoles_TenantRoleId" FOREIGN KEY ("TenantRoleId") REFERENCES "TenantRoles" ("Id") ON DELETE RESTRICT;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_TenantId_Email" ON "Users" ("TenantId", "Email");
CREATE INDEX IF NOT EXISTS "IX_Users_TenantRoleId" ON "Users" ("TenantRoleId");

ALTER TABLE "Clients" ALTER COLUMN "Id" SET NOT NULL, ALTER COLUMN "TenantId" SET NOT NULL;
ALTER TABLE "Clients" ADD CONSTRAINT "PK_Clients" PRIMARY KEY ("Id");
ALTER TABLE "Clients" ADD CONSTRAINT "FK_Clients_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE;
CREATE INDEX IF NOT EXISTS "IX_Clients_TenantId" ON "Clients" ("TenantId");

ALTER TABLE "Properties" ALTER COLUMN "Id" SET NOT NULL, ALTER COLUMN "TenantId" SET NOT NULL;
ALTER TABLE "Properties" ADD CONSTRAINT "PK_Properties" PRIMARY KEY ("Id");
ALTER TABLE "Properties" ADD CONSTRAINT "FK_Properties_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE;
ALTER TABLE "Properties" ADD CONSTRAINT "FK_Properties_Clients_ClientId" FOREIGN KEY ("ClientId") REFERENCES "Clients" ("Id") ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS "IX_Properties_TenantId" ON "Properties" ("TenantId");
CREATE INDEX IF NOT EXISTS "IX_Properties_ClientId" ON "Properties" ("ClientId");

ALTER TABLE "Payments" ALTER COLUMN "Id" SET NOT NULL, ALTER COLUMN "TenantId" SET NOT NULL;
ALTER TABLE "Payments" ADD CONSTRAINT "PK_Payments" PRIMARY KEY ("Id");
ALTER TABLE "Payments" ADD CONSTRAINT "FK_Payments_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE;
ALTER TABLE "Payments" ADD CONSTRAINT "FK_Payments_Clients_ClientId" FOREIGN KEY ("ClientId") REFERENCES "Clients" ("Id") ON DELETE SET NULL;
ALTER TABLE "Payments" ADD CONSTRAINT "FK_Payments_Properties_PropertyId" FOREIGN KEY ("PropertyId") REFERENCES "Properties" ("Id") ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS "IX_Payments_TenantId" ON "Payments" ("TenantId");
CREATE INDEX IF NOT EXISTS "IX_Payments_ClientId" ON "Payments" ("ClientId");
CREATE INDEX IF NOT EXISTS "IX_Payments_PropertyId" ON "Payments" ("PropertyId");

ALTER TABLE "InstallmentDues" ALTER COLUMN "Id" SET NOT NULL, ALTER COLUMN "TenantId" SET NOT NULL, ALTER COLUMN "ClientId" SET NOT NULL, ALTER COLUMN "PropertyId" SET NOT NULL;
ALTER TABLE "InstallmentDues" ADD CONSTRAINT "PK_InstallmentDues" PRIMARY KEY ("Id");
ALTER TABLE "InstallmentDues" ADD CONSTRAINT "FK_InstallmentDues_Clients_ClientId" FOREIGN KEY ("ClientId") REFERENCES "Clients" ("Id") ON DELETE RESTRICT;
ALTER TABLE "InstallmentDues" ADD CONSTRAINT "FK_InstallmentDues_Properties_PropertyId" FOREIGN KEY ("PropertyId") REFERENCES "Properties" ("Id") ON DELETE RESTRICT;
CREATE INDEX IF NOT EXISTS "IX_InstallmentDues_TenantId_Status_DueDate" ON "InstallmentDues" ("TenantId", "Status", "DueDate");
CREATE INDEX IF NOT EXISTS "IX_InstallmentDues_ClientId" ON "InstallmentDues" ("ClientId");
CREATE INDEX IF NOT EXISTS "IX_InstallmentDues_PropertyId" ON "InstallmentDues" ("PropertyId");

ALTER TABLE "Payments" ADD CONSTRAINT "FK_Payments_InstallmentDues_InstallmentDueId" FOREIGN KEY ("InstallmentDueId") REFERENCES "InstallmentDues" ("Id") ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS "IX_Payments_InstallmentDueId" ON "Payments" ("InstallmentDueId");
ALTER TABLE "InstallmentDues" ADD CONSTRAINT "FK_InstallmentDues_Payments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "Payments" ("Id") ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS "IX_InstallmentDues_PaymentId" ON "InstallmentDues" ("PaymentId");

ALTER TABLE "Expenses" ALTER COLUMN "Id" SET NOT NULL, ALTER COLUMN "TenantId" SET NOT NULL;
ALTER TABLE "Expenses" ADD CONSTRAINT "PK_Expenses" PRIMARY KEY ("Id");
ALTER TABLE "Expenses" ADD CONSTRAINT "FK_Expenses_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE;
CREATE INDEX IF NOT EXISTS "IX_Expenses_TenantId" ON "Expenses" ("TenantId");

-- ===================== PHASE 5: make each Id column an identity column, continuing from its current max =====================
DO $$
DECLARE tbl text;
BEGIN
    FOREACH tbl IN ARRAY ARRAY['Tenants','TenantRoles','Users','Clients','Properties','Payments','InstallmentDues','Expenses']
    LOOP
        EXECUTE format('ALTER TABLE %I ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY', tbl);
        EXECUTE format(
            'SELECT setval(pg_get_serial_sequence(''"%s"'', ''Id''), COALESCE((SELECT MAX("Id") FROM %I), 0) + 1, false)',
            tbl, tbl
        );
    END LOOP;
END $$;

COMMIT;
