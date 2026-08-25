using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClearWise.Api.Migrations
{
    /// <summary>
    /// The one deliberate, narrow bypass of row level security.
    /// </summary>
    /// <remarks>
    /// Sign-in is the only operation that cannot already know its tenant — establishing it is
    /// the point. RLS would therefore hide every user row and nobody could ever authenticate.
    /// <para>
    /// Rather than grant the application any broader reach, this function runs as its owner
    /// and returns <b>one</b> account matched on exact email, and only the columns
    /// authentication needs. It cannot list users, cannot search, and cannot reach any other
    /// table. Everything after sign-in is scoped by the tenant claim in the issued token.
    /// </para>
    /// </remarks>
    public partial class Auth_LoginResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION clearwise_resolve_login(p_email text)
                RETURNS TABLE (
                    id uuid,
                    tenant_id uuid,
                    email character varying(320),
                    display_name character varying(200),
                    password_hash text,
                    is_active boolean,
                    security_stamp integer
                )
                LANGUAGE sql
                SECURITY DEFINER
                -- Pinned so the definer's rights cannot be redirected at a shadowed object
                -- by a caller controlling search_path.
                SET search_path = public, pg_temp
                STABLE
                AS $fn$
                    SELECT u.id, u.tenant_id, u.email, u.display_name,
                           u.password_hash, u.is_active, u.security_stamp
                    FROM users u
                    WHERE lower(u.email) = lower(p_email)
                    LIMIT 1;
                $fn$;
                """);

            // Revoked from PUBLIC first: a SECURITY DEFINER function is executable by
            // everyone by default, which would hand the hash lookup to any role at all.
            migrationBuilder.Sql(
                "REVOKE ALL ON FUNCTION clearwise_resolve_login(text) FROM PUBLIC;");
            migrationBuilder.Sql(
                "GRANT EXECUTE ON FUNCTION clearwise_resolve_login(text) TO clearwise_app;");

            // Email must be unique per tenant already; this makes it unique globally, so the
            // lookup above can never be ambiguous about which account a sign-in refers to.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ix_users_email_lower ON users (lower(email));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_users_email_lower;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS clearwise_resolve_login(text);");
        }
    }
}
