namespace Accounting.Api.Exceptions;

/// <summary>No entity with the given identifier exists in the current tenant. Maps to 404.</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>
/// The request was understood but is not a valid accounting operation — an unbalanced
/// entry, a posting to a heading account, a closed period. Maps to 400.
/// </summary>
public class PostingValidationException(string message) : Exception(message);

/// <summary>
/// Credentials were missing, wrong, or the account cannot sign in. Maps to 401.
/// </summary>
/// <remarks>
/// The message is deliberately identical whether the email is unknown or the password is
/// wrong. Distinguishing them tells an attacker which addresses are worth guessing against.
/// </remarks>
public class AuthenticationFailedException(string message) : Exception(message);

/// <summary>
/// The database refused an operation that would have broken a ledger invariant.
/// </summary>
/// <remarks>
/// This should be rare: the service validates the same rules before attempting to persist,
/// so reaching here means either a race or a gap between the service's checks and the
/// database's. Both are worth knowing about, which is why it is a distinct type rather
/// than folded into <see cref="PostingValidationException"/>. Maps to 409.
/// </remarks>
public class LedgerIntegrityException(string message, Exception inner)
    : Exception(message, inner);
