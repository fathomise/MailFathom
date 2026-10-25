// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetireTheSpamScannerVerdicts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The scanner stage is gone, so nothing an earlier release recorded from it or for it may outlive it. The
            // model does not change: the corpus column stays mapped, because a replica of the previous release still
            // reads it during a rolling upgrade, and is only emptied here.
            //
            // A verdict the scanner decided goes with its signals, which the foreign key removes in cascade, so that
            // mail is classified again by the deterministic stage on its next run, and until then the derived-work gate
            // admits it once the classification wait has passed. A verdict the deterministic stage decided keeps its own
            // signals and loses only the scanner rules beside them, which were recorded after every other signal, so the
            // ordinals left behind stay contiguous.
            //
            // The profile digest no longer carries any threshold, so a run still outstanding would end as superseded
            // under the terms now in force; its profile is cleared instead, which a pass reads as a run that adopts the
            // current one.
            //
            // The settings the stage was switched and judged by are refused wherever they are bound: in the deployment's
            // persisted document a leftover key stops the start, in an account's document it holds the account back, and
            // in a settings policy it makes the whole policy unreadable, which holds back every user it governs. So they
            // are removed from all three, read the way the host binds them — property names in any case, a path written
            // nested or joined with colons — and each document's version moves on, as any write of it does, so a cached
            // reading is retired.
            migrationBuilder.Sql(
                """
                DELETE FROM email_spam_classifications WHERE "DecidedBy" = 'Scanner';

                DELETE FROM email_spam_classification_signals
                WHERE "Kind" = 'ScannerRule' OR "Source" = 'ScannerCorpus';

                UPDATE email_spam_classifications SET "CorpusRevision" = NULL WHERE "CorpusRevision" IS NOT NULL;

                UPDATE spam_classification_runs SET "Profile" = NULL WHERE "EndedAt" IS NULL AND "Profile" IS NOT NULL;

                CREATE FUNCTION pg_temp.mf_without_members(entry jsonb, names text[]) RETURNS jsonb
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT CASE WHEN jsonb_typeof(entry) = 'object' THEN (
                        SELECT coalesce(jsonb_object_agg(member.key, member.value), '{}'::jsonb)
                        FROM jsonb_each(entry) AS member
                        WHERE lower(member.key) <> ALL (names))
                    ELSE entry END
                $$;

                CREATE FUNCTION pg_temp.mf_classification_without_scanner(block jsonb) RETURNS jsonb
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT CASE WHEN jsonb_typeof(block) = 'object' THEN (
                        SELECT coalesce(jsonb_object_agg(
                            member.key,
                            CASE WHEN lower(member.key) = 'actions'
                                THEN pg_temp.mf_without_members(member.value, ARRAY['threshold'])
                                ELSE member.value
                            END), '{}'::jsonb)
                        FROM jsonb_each(block) AS member
                        WHERE lower(member.key) NOT IN ('usescanner', 'scannerthreshold', 'scanner', 'actions:threshold')
                          AND lower(member.key) NOT LIKE 'scanner:%')
                    ELSE block END
                $$;

                CREATE FUNCTION pg_temp.mf_settings_without_scanner(document jsonb) RETURNS jsonb
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT CASE WHEN jsonb_typeof(document) = 'object' THEN (
                        SELECT coalesce(jsonb_object_agg(
                            member.key,
                            CASE WHEN lower(member.key) = 'spamclassification'
                                THEN pg_temp.mf_classification_without_scanner(member.value)
                                ELSE member.value
                            END), '{}'::jsonb)
                        FROM jsonb_each(document) AS member
                        WHERE lower(member.key) NOT IN (
                                'spamclassification:usescanner',
                                'spamclassification:scannerthreshold',
                                'spamclassification:scanner',
                                'spamclassification:actions:threshold')
                          AND lower(member.key) NOT LIKE 'spamclassification:scanner:%')
                    ELSE document END
                $$;

                CREATE FUNCTION pg_temp.mf_editing_without_scanner(editing jsonb) RETURNS jsonb
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT CASE WHEN jsonb_typeof(editing) = 'object' THEN (
                        SELECT coalesce(jsonb_object_agg(
                            member.key,
                            CASE WHEN lower(member.key) = 'properties' AND jsonb_typeof(member.value) = 'array' THEN (
                                SELECT coalesce(jsonb_agg(path.value ORDER BY path.position), '[]'::jsonb)
                                FROM jsonb_array_elements(member.value) WITH ORDINALITY AS path(value, position)
                                WHERE jsonb_typeof(path.value) <> 'string'
                                   OR lower(path.value #>> '{}') NOT IN (
                                        'spamclassification:usescanner',
                                        'spamclassification:scannerthreshold',
                                        'spamclassification:actions:threshold'))
                            ELSE member.value END), '{}'::jsonb)
                        FROM jsonb_each(editing) AS member)
                    ELSE editing END
                $$;

                CREATE FUNCTION pg_temp.mf_policy_without_scanner(policy jsonb) RETURNS jsonb
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT CASE WHEN jsonb_typeof(policy) = 'object' THEN (
                        SELECT coalesce(jsonb_object_agg(
                            section.key,
                            CASE WHEN lower(section.key) = 'mailaccounts' AND jsonb_typeof(section.value) = 'object' THEN (
                                SELECT coalesce(jsonb_object_agg(
                                    statement.key,
                                    CASE
                                        WHEN lower(statement.key) IN ('defaults', 'forced')
                                            THEN pg_temp.mf_settings_without_scanner(statement.value)
                                        WHEN lower(statement.key) = 'editing'
                                            THEN pg_temp.mf_editing_without_scanner(statement.value)
                                        ELSE statement.value
                                    END), '{}'::jsonb)
                                FROM jsonb_each(section.value) AS statement)
                            ELSE section.value END), '{}'::jsonb)
                        FROM jsonb_each(policy) AS section)
                    ELSE policy END
                $$;

                UPDATE settings_root
                SET "Document" = pg_temp.mf_settings_without_scanner("Document"),
                    "Version" = "Version" + 1
                WHERE pg_temp.mf_settings_without_scanner("Document") <> "Document";

                UPDATE settings_mail_accounts
                SET "Document" = pg_temp.mf_settings_without_scanner("Document"),
                    "Version" = "Version" + 1
                WHERE pg_temp.mf_settings_without_scanner("Document") <> "Document";

                UPDATE settings_policies
                SET "Document" = pg_temp.mf_policy_without_scanner("Document"),
                    "Version" = "Version" + 1
                WHERE pg_temp.mf_policy_without_scanner("Document") <> "Document";

                DROP FUNCTION pg_temp.mf_policy_without_scanner(jsonb);
                DROP FUNCTION pg_temp.mf_editing_without_scanner(jsonb);
                DROP FUNCTION pg_temp.mf_settings_without_scanner(jsonb);
                DROP FUNCTION pg_temp.mf_classification_without_scanner(jsonb);
                DROP FUNCTION pg_temp.mf_without_members(jsonb, text[]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // What Up removed was derived from or configured for a stage that no longer exists, so there is nothing to
            // restore it from.
        }
    }
}
