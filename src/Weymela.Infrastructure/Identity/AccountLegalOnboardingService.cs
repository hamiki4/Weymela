using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Operations;

namespace Weymela.Infrastructure.Identity;

public sealed record AccountLegalDocument(Guid DocumentId, string Kind, string Title, string Version,
    string ContentHash, DateTime EffectiveFromUtc, string ViewPath, bool Accepted);
public sealed record AccountLegalStatus(bool Available, bool Current, IReadOnlyList<AccountLegalDocument> Documents, bool Required = true);
public sealed record AccountLegalDocumentConfirmation(Guid DocumentId, string ContentHash, bool Accepted);
public sealed record AccountLegalConfirmation(AccountLegalDocumentConfirmation? TermsOfService,
    AccountLegalDocumentConfirmation? PrivacyPolicy);

public sealed class AccountLegalOnboardingService(WeymelaDbContext db, TimeProvider clock,
    RuntimeOptions? options = null, AccountLegalDocumentSource? legalSource = null)
{
    private static readonly LegalDocumentType[] Required =
        [LegalDocumentType.TermsOfService, LegalDocumentType.PrivacyPolicy];

    public async Task<bool> HasCurrentAcceptanceAsync(Guid userId, CancellationToken ct)
        => options?.LegalConsentDeferred == true
            || (await StatusAsync(userId, ct, "en")).Current
            || (await StatusAsync(userId, ct, "am")).Current;

    public async Task<AccountLegalStatus> PublicationAsync(CancellationToken ct, string language = "en")
    {
        var documents = await EffectiveDocumentsAsync(ct, language);
        return documents is null
            ? new(false, false, [], Required: options?.LegalConsentDeferred != true)
            : new(true, false, documents.Select(x => new AccountLegalDocument(x.Id, x.Type.ToString(), Title(x.Type),
                x.Version, x.ContentHash, x.EffectiveFromUtc, ViewPath(x.Type) + (language == "am" ? "?language=am" : ""), false)).ToList(),
                Required: options?.LegalConsentDeferred != true);
    }

    public async Task<AccountLegalStatus> StatusAsync(Guid userId, CancellationToken ct, string language = "en")
    {
        if (userId == Guid.Empty) throw new ApplicationFailure(FailureKind.Forbidden, "Sign in to your account.");
        if (options?.LegalConsentDeferred == true) return new(false, false, [], Required: false);
        var documents = await EffectiveDocumentsAsync(ct, language);
        if (documents is null)
            return new(false, false, []);
        var ids = documents.Select(x => x.Id).ToArray();
        var accepted = await db.LegalAcceptances.AsNoTracking()
            .Where(x => x.UserId == userId && x.Role == LegalRole.Account && ids.Contains(x.DocumentVersionId))
            .Select(x => x.DocumentVersionId).ToListAsync(ct);
        var acceptedIds = accepted.ToHashSet();
        var result = documents.Select(x => new AccountLegalDocument(x.Id, x.Type.ToString(), Title(x.Type),
            x.Version, x.ContentHash, x.EffectiveFromUtc, ViewPath(x.Type) + (language == "am" ? "?language=am" : ""), acceptedIds.Contains(x.Id))).ToList();
        return new(true, result.All(x => x.Accepted), result);
    }

    public async Task AcceptCurrentAsync(Guid userId, AccountLegalConfirmation? confirmation,
        string? ipReference, string? userAgentReference, CancellationToken ct)
    {
        if (options?.LegalConsentDeferred == true) return;
        if (confirmation?.TermsOfService is null || confirmation.PrivacyPolicy is null
            || !confirmation.TermsOfService.Accepted || !confirmation.PrivacyPolicy.Accepted)
            throw new ApplicationFailure(FailureKind.Validation, "Accept the current Terms of Service and Privacy Policy.");
        var submittedTerms = await db.LegalDocumentVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == confirmation.TermsOfService.DocumentId, ct);
        var language = submittedTerms?.Version.EndsWith("-am", StringComparison.Ordinal) == true ? "am" : "en";
        var documents = await CurrentDocumentsAsync(ct, language);
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

    private async Task<IReadOnlyList<LegalDocumentVersion>> CurrentDocumentsAsync(CancellationToken ct, string language)
        => await EffectiveDocumentsAsync(ct, language)
            ?? throw new ApplicationFailure(FailureKind.Validation,
                "Account activation requires published Terms of Service and Privacy Policy.", code: "ApprovedLegalUnavailable");

    private async Task<IReadOnlyList<LegalDocumentVersion>?> EffectiveDocumentsAsync(CancellationToken ct, string language)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (options?.EnvironmentName == "Production")
            foreach (var type in Required)
                foreach (var publishedLanguage in new[] { "en", "am" })
                    if (legalSource is null || await legalSource.ReadAsync(db, type, publishedLanguage, now, ct) is null)
                        return null;
        if (language is not ("en" or "am"))
            throw new ApplicationFailure(FailureKind.Validation, "Choose English or Amharic for the published policies.");
        var production = options?.EnvironmentName == "Production";
        var suffix = "-" + language;
        var effective = await db.LegalDocumentVersions.AsNoTracking()
            .Where(x => Required.Contains(x.Type) && x.EffectiveFromUtc <= now
                && (!production || x.Version.EndsWith(suffix)))
            .OrderByDescending(x => x.EffectiveFromUtc).ThenByDescending(x => x.Version).ThenByDescending(x => x.Id)
            .ToListAsync(ct);
        var current = Required.Select(type => effective.FirstOrDefault(x => x.Type == type)).ToList();
        if (current.Any(x => x is null || string.IsNullOrWhiteSpace(x.ContentHash)))
            return null;
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
