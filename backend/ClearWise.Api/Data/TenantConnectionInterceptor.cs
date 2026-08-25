using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ClearWise.Api.Data;

/// <summary>
/// Pushes the current tenant into the PostgreSQL session as <c>app.current_tenant</c>,
/// which is what every row level security policy filters on.
/// </summary>
/// <remarks>
/// This runs on every connection open rather than once at startup, because Npgsql pools
/// physical connections and hands the same one to different requests. Setting it once
/// would leak one tenant's setting into another tenant's request.
/// <para>
/// When no tenant is set the value is written as an empty string, which matches no row.
/// Failing closed matters more than a helpful error: the alternative — leaving the
/// previous value in place — would serve another tenant's data.
/// </para>
/// </remarks>
public sealed class TenantConnectionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    private const string SetTenantSql = "SELECT set_config('app.current_tenant', $1, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = SetTenantSql;

        var parameter = command.CreateParameter();
        parameter.Value = tenantContext.TenantId?.ToString() ?? string.Empty;
        command.Parameters.Add(parameter);

        return command;
    }
}
