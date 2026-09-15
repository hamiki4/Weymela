using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;

namespace Weymela.Infrastructure.Identity;

public sealed record AccountLegalDocument(Guid DocumentId, string Kind, string Title, string Version,
    string ContentHash, DateTime EffectiveFromUtc, string ViewPath, bool Accepted);
public sealed record AccountLegalStatus(bool Current, IReadOnlyList<AccountLegalDocument> Documents);
public sealed record AccountLegalDocumentConfirmation(Guid DocumentId, string ContentHash, bool Accepted);
public sealed record AccountLegalConfirmation(AccountLegalDocumentConfirmation? TermsOfService,
    AccountLegalDocumentConfirmation? PrivacyPolicy);

public sealed class AccountLegalOnboardingService(WeymelaDbContext db, TimeProvider clock)
{
    private static readonly LegalDocumentType[] Required =
        [LegalDocumentType.TermsOfService, LegalDocumentType.PrivacyPolicy];

    public async Task<AccountLegalStatus> StatusAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) throw new ApplicationFailure(FailureKind.Forbidden, "Sign in to your account.");
        var documents = await CurrentDocumentsAsync(ct);
        var ids = documents.Select(x => x.Id).ToArray();
        var accepted = await db.LegalAcceptances.AsNoTracking()
            .Where(x => x.UserId == userId && x.Role == LegalRole.Account && ids.Contains(x.DocumentVersionId))
            .Select(x => x.DocumentVersionId).ToListAsync(ct);
        var acceptedIds = accepted.ToHashSet();
        var result = documents.Select(x => new AccountLegalDocument(x.Id, x.Type.ToString(), Title(x.Type),
            x.Version, x.ContentHash, x.EffectiveFromUtc, ViewPath(x.Type), acceptedIds.Contains(x.Id))).ToList();
        return new(result.All(x => x.Accepted), result);
    }

    public async Task AcceptCurrentAsync(Guid userId, AccountLegalConfirmation? confirmation,
        string? ipReference, string? userAgentReference, CancellationToken ct)
    {
        if (confirmation?.TermsOfService is null || confirmation.PrivacyPolicy is null
            || !confirmation.TermsOfService.Accepted || !confirmation.PrivacyPolicy.Accepted)
            throw new ApplicationFailure(FailureKind.Validation, "Accept the current Terms of Service and Privacy Policy.");
        var documents = await CurrentDocumentsAsync(ct);
        Validate(documents.Single(x => x.Type == LegalDocumentType.TermsOfService), confirmation.TermsOfService);
        Validate(documents.Single(x => x.Type == LegalDocumentType.PrivacyPolicy), confirmation.PrivacyPolicy);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var document in documents)
        {
            if (!await db.LegalAcceptances.AnyAsync(x => x.UserId == userId && x.Role == LegalRole.Account
                && x.DocumentVersionId == document.Id, ct))
            {
                db.LegalAcceptances.Add(new LegalAcceptance(userId, LegalRole.Account, document.Id, now,
                    SafeReference(ipReference, 120), SafeReference(userAgentReference, 300)));
                db.AuditEvents.Add(new(Guid.NewGuid(), "AccountLegalVersionAccepted", userId, null, null, null,
                    Guid.NewGuid(), now, $"documentVersion={document.Id:D};type={document.Type}"));
            }
        }
    }

    private async Task<IReadOnlyList<LegalDocumentVersion>> CurrentDocumentsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var effective = await db.LegalDocumentVersions.AsNoTracking()
            .Where(x => Required.Contains(x.Type) && x.EffectiveFromUtc <= now)
            .OrderByDescending(x => x.EffectiveFromUtc).ThenByDescending(x => x.Version).ThenByDescending(x => x.Id)
            .ToListAsync(ct);
        var current = Required.Select(type => effective.FirstOrDefault(x => x.Type == type)).ToList();
        if (current.Any(x => x is null || string.IsNullOrWhiteSpace(x.ContentHash)))
            throw new ApplicationFailure(FailureKind.Validation, "Current legal documents are unavailable.");
        return current.Cast<LegalDocumentVersion>().ToList();
    }

    private static void Validate(LegalDocumentVersion current, AccountLegalDocumentConfirmation submitted)
    {
        if (submitted.DocumentId != current.Id || !string.Equals(submitted.ContentHash, current.ContentHash, StringComparison.Ordinal))
            throw new ApplicationFailure(FailureKind.Validation, "The legal documents changed. Review and accept the current versions.");
    }

    private static string Title(LegalDocumentType type) => type == LegalDocumentType.TermsOfService
        ? "Terms of Service" : "Privacy Policy";
    private static string ViewPath(LegalDocumentType type) => type == LegalDocumentType.TermsOfService
        ? "/legal/terms-of-service" : "/legal/privacy-policy";
    private static string? SafeReference(string? value, int max) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
