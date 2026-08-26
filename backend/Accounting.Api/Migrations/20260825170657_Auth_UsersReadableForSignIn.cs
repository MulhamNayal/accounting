using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Api.Migrations
{
    /// <summary>
    /// Lets the sign-in lookup actually work.
    /// </summary>
    /// <remarks>
    /// <c>users</c> was FORCEd, which applies the policy to the table's owner as well. That
    /// defeats the whole point of a SECURITY DEFINER lookup: the function runs as the owner,
    /// the owner is still policed, no tenant is set during sign-in, and so no row is ever
    /// visible. Nobody could authenticate.
    /// <para>
    /// Dropping FORCE leaves the policy fully in effect for <c>clearwise_app</c> â€” the role
    /// the application actually uses â€” while allowing the owner-owned function to resolve one
    /// account by email. This is the same reasoning already applied to <c>tenants</c>:
    /// provisioning a tenant and authenticating into one are both inherently cross-tenant
    /// operations, and both are performed by the owner rather than the application.
    /// </para>
    /// </remarks>
    public partial class Auth_UsersReadableForSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE users NO FORCE ROW LEVEL SECURITY;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE users FORCE ROW LEVEL SECURITY;");
        }
    }
}
