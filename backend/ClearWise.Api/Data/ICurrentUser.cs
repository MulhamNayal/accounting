namespace ClearWise.Api.Data;

/// <summary>
/// Who is acting. Every posted entry records this, and it is the only answer to "who put
/// this figure in the books".
/// </summary>
/// <remarks>
/// Like <see cref="ITenantContext"/>, this is populated per request. Until authentication
/// exists, Development fills it with the seeded demo user; the real implementation reads it
/// from the authenticated principal. Outside Development it stays empty and posting fails,
/// which is the correct direction — an unattributable entry is worse than no entry.
/// </remarks>
public interface ICurrentUser
{
    Guid? UserId { get; }

    void SetUser(Guid userId);
}

public sealed class CurrentUser : ICurrentUser
{
    public Guid? UserId { get; private set; }

    public void SetUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id must not be empty.", nameof(userId));
        }

        UserId = userId;
    }
}
