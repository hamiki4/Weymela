using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Transactions;
using Weymela.Infrastructure.Web;

namespace Weymela.Infrastructure.Finance;

public sealed class UgcService(WeymelaDbContext db, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private EfUnitOfWork Transaction => new(db, IsolationLevel.Serializable);

    public Task<Guid> CreateAsync(Actor actor, CreateUgcInput input, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandBusiness(actor, token);
            var fingerprint = RequestFingerprint.Create(JsonSerializer.Serialize(input));
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "CreateUgc", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            if (!Enum.TryParse<UgcContentType>(input.ContentType, true, out var contentType) || !Enum.IsDefined(contentType))
                throw new ApplicationFailure(FailureKind.Validation, "Choose Video or Photos.");
            var resources = Resources(input.Resources);
            var requirements = Requirements(input.PlatformRequirements);
            var capacities = Capacities(input.PlatformCapacities);
            if (input.PlatformCapacities is not null && requirements.Count > 0 && capacities.Count == 0)
                throw new ApplicationFailure(FailureKind.Validation, "Choose Creator slots for each posting platform.");
            if (capacities.Count > 0 && (capacities.Sum(x => x.Capacity) != input.CreatorsNeeded
                || requirements.Select(x => x.Platform).Except(capacities.Select(x => x.Platform)).Any()
                || capacities.Select(x => x.Platform).Except(requirements.Select(x => x.Platform)).Any()))
                throw new ApplicationFailure(FailureKind.Validation, "Posting platforms must match Creator slots.");
            var config = await new FinancialConfigurationResolver(db).EffectiveAsync(Now, token);
            var pricing = config.Ugc
                ?? throw new InvalidOperationException("The effective financial configuration does not include UGC settings.");
            var opportunity = new UgcOpportunity(actor.BusinessId!.Value, input.Title, input.Slogan, contentType,
                input.Instructions, JsonSerializer.Serialize(resources), input.Location, input.DueDateUtc,
                input.ProductProvided, input.CreatorMustPurchase, input.UsageRights, Amount(input.CreatorPayment),
                input.CreatorsNeeded, pricing, Now, requirements, capacities, input.ApplicationClosesAtUtc);
            db.UgcOpportunities.Add(opportunity);
            if (input.CustomerOfferEnabled)
            {
                if (requirements.Count == 0 || capacities.Count == 0)
                    throw new ApplicationFailure(FailureKind.Validation,
                        "UGC + Sales requires Creator publication on a selected social platform.");
                if (pricing.CustomerOfferPlatformSalePercent is not { } platformSalePercent)
                    throw new ApplicationFailure(FailureKind.Validation, "The effective financial configuration does not support UGC Customer Offers.");
                var offer = CreateCustomerOffer(opportunity, input.CustomerDiscountPercent,
                    input.CustomerOfferFundedAllocation, input.CustomerFacingSlogan,
                    input.CustomerOfferStartsAtUtc, input.CustomerOfferEndsAtUtc,
                    platformSalePercent, pricing.EffectiveFromUtc, pricing.ConfigurationVersionId);
                db.UgcCustomerOffers.Add(offer);
            }
            db.UgcRevisions.Add(new(opportunity.Id, 1, false, Snapshot(opportunity), actor.UserId, Now));
            operation.Remember(actor, "CreateUgc", key, fingerprint, opportunity.Id.ToString(), Now);
            Record(actor, "UgcCreated", opportunity.Id, null, Guid.NewGuid(), new { opportunity.Id, opportunity.Title });
            return opportunity.Id;
        }, ct);

    public Task<Guid> PublishAsync(Actor actor, Guid id, long expectedVersion, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandBusiness(actor, token);
            var fingerprint = RequestFingerprint.Create(id.ToString(), expectedVersion.ToString());
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "PublishUgc", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var opportunity = await Opportunity(id, token);
            Own(actor, opportunity);
            if (opportunity.Version != expectedVersion) throw Conflict();
            if (opportunity.ProductProvided == opportunity.CreatorMustPurchase)
                throw new ApplicationFailure(FailureKind.Validation, "Choose one Product arrangement before publishing UGC.");
            var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == opportunity.BusinessId, token);
            var offer = await db.UgcCustomerOffers.SingleOrDefaultAsync(x => x.UgcOpportunityId == opportunity.Id, token);
            var correlation = Guid.NewGuid();
            try
            {
                opportunity.Publish(wallet, Now, correlation);
                offer?.Publish(wallet, Now, correlation);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("negative", StringComparison.OrdinalIgnoreCase))
            { throw new ApplicationFailure(FailureKind.InsufficientFunds, "Available balance is insufficient to publish this UGC opportunity.", ex); }
            var journal = Journal(actor, JournalSourceType.UgcReservation, opportunity.RequiredFunding,
                "BusinessAvailable", "UgcAllocatedReserve", key, correlation, opportunity.Id, null);
            db.UgcReservations.Add(new(opportunity.Id, opportunity.BusinessId, opportunity.RequiredFunding, journal.Id, Now));
            db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, opportunity.RequiredFunding, "UgcReserve", journal.Id, Now, opportunity.Id));
            db.UgcBudgetEntries.Add(new(Guid.NewGuid(), opportunity.Id, null, opportunity.RequiredFunding, "Reserved", journal.Id, Now));
            if (offer is not null)
            {
                var offerJournal = Journal(actor, JournalSourceType.UgcCustomerOfferReservation, offer.FundedLimit,
                    "BusinessAvailable", "UgcCustomerOfferReserve", key, correlation, opportunity.Id, null, offer.Id);
                db.UgcCustomerOfferReservations.Add(new(offer.Id, opportunity.BusinessId, offer.FundedLimit, offerJournal.Id, Now));
                db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, offer.FundedLimit,
                    "UgcCustomerOfferReserve", offerJournal.Id, Now, opportunity.Id, offer.Id));
                db.UgcCustomerOfferBudgetEntries.Add(new(Guid.NewGuid(), offer.Id, null, offer.FundedLimit,
                    "Reserved", offerJournal.Id, Now));
            }
            operation.Remember(actor, "PublishUgc", key, fingerprint, opportunity.Id.ToString(), Now);
            Record(actor, "UgcPublished", opportunity.Id, null, correlation,
                new { opportunity.Id, opportunity.BusinessId, RequiredFunding = opportunity.RequiredFunding.Amount });
            return opportunity.Id;
        }, ct);

    public Task<Guid> RequestAsync(Actor actor, Guid id, string key, CancellationToken ct,
        string? selectedPlatform = null, Guid? verifiedSocialProfileId = null) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandCreator(actor, token);
            var fingerprint = selectedPlatform is null && verifiedSocialProfileId is null
                ? RequestFingerprint.Create(id.ToString(), actor.CreatorId!.Value.ToString())
                : RequestFingerprint.Create(id.ToString(), actor.CreatorId!.Value.ToString(),
                    selectedPlatform ?? "", verifiedSocialProfileId?.ToString() ?? "");
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "RequestUgc", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var opportunity = await Opportunity(id, token);
            if (opportunity.Status is not (UgcOpportunityStatus.Open or UgcOpportunityStatus.InProgress)
                || opportunity.ReservedFunding.Amount + opportunity.UsedFunding.Amount < opportunity.RequiredFunding.Amount
                || opportunity.DueDateUtc <= Now || opportunity.ApplicationClosesAtUtc is { } closes && Now >= closes || opportunity.ApprovedCreatorCount >= opportunity.CreatorCapacity
                || opportunity.ProductProvided == opportunity.CreatorMustPurchase)
                throw new ApplicationFailure(FailureKind.Validation, "This UGC opportunity is not accepting requests.");
            if (!await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.Role == ActorRole.Business
                    && x.SubjectId == opportunity.BusinessId && x.IsActive, token))
                throw new ApplicationFailure(FailureKind.Validation, "This UGC opportunity is not accepting requests.");
            var enforceAudience = (await new FinancialConfigurationResolver(db).EffectiveAsync(Now, token)).EnforceAudienceRequirements;
            await EnsureCreatorEligibility(actor.CreatorId.Value, opportunity, enforceAudience, token);
            CreatorPlatform? platform = null;
            if (HasPlatformSlots(opportunity))
            {
                if (!Enum.TryParse<CreatorPlatform>(selectedPlatform, true, out var chosen) || !Enum.IsDefined(chosen)
                    || verifiedSocialProfileId is null
                    || !opportunity.PlatformCapacities.Any(x => x.Platform == chosen && x.Available > 0))
                    throw new ApplicationFailure(FailureKind.Validation, "Choose an available verified Creator platform.");
                await EnsureEligibleProfile(actor.CreatorId.Value, chosen, verifiedSocialProfileId.Value, opportunity, enforceAudience, token);
                platform = chosen;
            }
            else if (selectedPlatform is not null || verifiedSocialProfileId is not null)
                throw new ApplicationFailure(FailureKind.Validation, "This UGC opportunity uses general Creator capacity.");
            if (await db.UgcCreatorRequests.AnyAsync(x => x.UgcOpportunityId == id && x.CreatorId == actor.CreatorId
                    && (x.Status == UgcRequestStatus.Pending || x.Status == UgcRequestStatus.Approved), token))
                throw new ApplicationFailure(FailureKind.Validation, "You already requested this UGC opportunity.");
            var request = new UgcCreatorRequest(id, actor.CreatorId.Value, Now, platform, verifiedSocialProfileId);
            db.UgcCreatorRequests.Add(request);
            operation.Remember(actor, "RequestUgc", key, fingerprint, request.Id.ToString(), Now);
            Record(actor, "UgcRequestReceived", id, actor.CreatorId, Guid.NewGuid(),
                new { OpportunityId = id, RequestId = request.Id, opportunity.BusinessId, CreatorId = actor.CreatorId.Value });
            return request.Id;
        }, ct);

    public Task<Guid> ReviewRequestAsync(Actor actor, Guid requestId, bool approve, string? reason, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandBusiness(actor, token);
            var fingerprint = RequestFingerprint.Create(requestId.ToString(), approve.ToString(), reason?.Trim() ?? "");
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "ReviewUgcRequest", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var request = await db.UgcCreatorRequests.SingleOrDefaultAsync(x => x.Id == requestId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC request not found.");
            var opportunity = await Opportunity(request.UgcOpportunityId, token); Own(actor, opportunity);
            Guid result;
            if (approve)
            {
                if (HasPlatformSlots(opportunity))
                {
                    if (request.SelectedPlatform is not { } chosen || request.VerifiedSocialProfileId is not { } profile)
                        throw new ApplicationFailure(FailureKind.Validation, "This UGC request has no verified platform selection.");
                    var enforceAudience = (await new FinancialConfigurationResolver(db).EffectiveAsync(Now, token)).EnforceAudienceRequirements;
                    await EnsureEligibleProfile(request.CreatorId, chosen, profile, opportunity, enforceAudience, token);
                    if (!opportunity.PlatformCapacities.Any(x => x.Platform == chosen && x.Available > 0))
                        throw new ApplicationFailure(FailureKind.Validation, "This UGC platform is full.");
                }
                opportunity.ApproveCreator(request.SelectedPlatform); request.Approve(actor.UserId, Now);
                var assignment = new UgcAssignment(opportunity.Id, request.Id, request.CreatorId,
                    opportunity.CreatorPayment, opportunity.PerAssignmentFee, opportunity.CurrentRevision, Now, opportunity.DueDateUtc);
                db.UgcAssignments.Add(assignment); result = assignment.Id;
                Record(actor, "UgcRequestApproved", opportunity.Id, request.CreatorId, Guid.NewGuid(),
                    new { OpportunityId = opportunity.Id, RequestId = request.Id, AssignmentId = assignment.Id, CreatorId = request.CreatorId });
            }
            else
            {
                request.Reject(actor.UserId, reason, Now); result = request.Id;
                Record(actor, "UgcRequestRejected", opportunity.Id, request.CreatorId, Guid.NewGuid(),
                    new { OpportunityId = opportunity.Id, RequestId = request.Id, CreatorId = request.CreatorId, Reason = Clean(reason, 1000) });
            }
            operation.Remember(actor, "ReviewUgcRequest", key, fingerprint, result.ToString(), Now);
            return result;
        }, ct);

    public Task<Guid> SubmitAsync(Actor actor, Guid assignmentId, UgcSubmissionInput input, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandCreator(actor, token);
            var url = ExternalUrl(input.SubmissionUrl);
            var fingerprint = RequestFingerprint.Create(assignmentId.ToString(), url);
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "SubmitUgc", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var assignment = await db.UgcAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC assignment not found.");
            if (assignment.CreatorId != actor.CreatorId) throw new ApplicationFailure(FailureKind.Forbidden, "This UGC assignment belongs to another Creator.");
            var opportunity = await Opportunity(assignment.UgcOpportunityId, token);
            var requirements = PostingRequirements(opportunity);
            if (requirements.Count > 0)
            {
                if (Now > opportunity.DueDateUtc)
                    throw new ApplicationFailure(FailureKind.Validation, "Social UGC content must be posted by the due date.");
                var request = await db.UgcCreatorRequests.AsNoTracking().SingleAsync(x => x.Id == assignment.UgcCreatorRequestId, token);
                var postingPlatforms = request.SelectedPlatform is { } selected
                    ? requirements.Where(x => x.Platform == selected).ToArray()
                    : requirements.ToArray();
                if (!MatchesRequiredSocialPlatform(url, postingPlatforms))
                    throw new ApplicationFailure(FailureKind.Validation, "Submit a secure social post link from one of the required platforms.");
            }
            var contentRevision = (await db.UgcSubmissions.Where(x => x.UgcAssignmentId == assignment.Id)
                .MaxAsync(x => (int?)x.ContentRevisionNumber, token) ?? 0) + 1;
            assignment.Submitted();
            var submission = new UgcSubmission(assignment.Id, assignment.AcceptedRevisionNumber, contentRevision, url, Now);
            db.UgcSubmissions.Add(submission);
            operation.Remember(actor, "SubmitUgc", key, fingerprint, submission.Id.ToString(), Now);
            Record(actor, "UgcContentSubmitted", opportunity.Id, assignment.CreatorId, Guid.NewGuid(),
                new { OpportunityId = opportunity.Id, AssignmentId = assignment.Id, SubmissionId = submission.Id, opportunity.BusinessId });
            return submission.Id;
        }, ct);

    public Task<Guid> SubmitPrivateAsync(Actor actor, Guid assignmentId, PrivateReviewMediaInput input,
        string key, CancellationToken ct) => Transaction.ExecuteAsync(async token =>
        {
            await DemandCreator(actor, token);
            var fingerprint = RequestFingerprint.Create(assignmentId.ToString(), input.Sha256,
                input.ContentType, input.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "SubmitPrivateUgc", key, fingerprint, token) is { } replay)
                return Guid.Parse(replay);
            var assignment = await db.UgcAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC assignment not found.");
            if (assignment.CreatorId != actor.CreatorId)
                throw new ApplicationFailure(FailureKind.Forbidden, "This UGC assignment belongs to another Creator.");
            var opportunity = await Opportunity(assignment.UgcOpportunityId, token);
            if (opportunity.DueDateUtc < Now)
                throw new ApplicationFailure(FailureKind.Validation, "The content deadline has passed.");
            if (opportunity.ContentType == UgcContentType.Video && input.ContentType != "video/mp4"
                || opportunity.ContentType == UgcContentType.Photos && input.ContentType is not ("image/jpeg" or "image/png"))
                throw new ApplicationFailure(FailureKind.Validation,
                    opportunity.ContentType == UgcContentType.Video ? "Choose an MP4 review video." : "Choose a JPEG or PNG review image.");
            var contentRevision = (await db.UgcSubmissions.Where(x => x.UgcAssignmentId == assignment.Id)
                .MaxAsync(x => (int?)x.ContentRevisionNumber, token) ?? 0) + 1;
            var asset = new PrivateReviewMediaAsset(assignment.CreatorId, opportunity.BusinessId, null,
                assignment.Id, contentRevision, input.StorageKey, input.ContentType, input.Length,
                input.Sha256, input.OriginalFileName, Now);
            var submission = new UgcSubmission(assignment.Id, assignment.AcceptedRevisionNumber,
                contentRevision, asset.Id, Now);
            assignment.Submitted();
            db.PrivateReviewMediaAssets.Add(asset);
            db.UgcSubmissions.Add(submission);
            operation.Remember(actor, "SubmitPrivateUgc", key, fingerprint, submission.Id.ToString(), Now);
            Record(actor, "UgcContentSubmitted", opportunity.Id, assignment.CreatorId, Guid.NewGuid(),
                new { OpportunityId = opportunity.Id, AssignmentId = assignment.Id,
                    SubmissionId = submission.Id, opportunity.BusinessId });
            return submission.Id;
        }, ct);

    public Task<Guid> ReviewSubmissionAsync(Actor actor, Guid assignmentId, string action, string? feedback, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandBusiness(actor, token);
            var normalized = action.Trim().ToLowerInvariant();
            if (normalized is not ("approve" or "changes" or "reject")) throw new ApplicationFailure(FailureKind.Validation, "Choose Approve, Request Changes, or Reject.");
            var fingerprint = RequestFingerprint.Create(assignmentId.ToString(), normalized, feedback?.Trim() ?? "");
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "ReviewUgcSubmission", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var assignment = await db.UgcAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC assignment not found.");
            var opportunity = await Opportunity(assignment.UgcOpportunityId, token); Own(actor, opportunity);
            var submission = await db.UgcSubmissions.Where(x => x.UgcAssignmentId == assignment.Id && x.Status == UgcAssignmentStatus.Submitted)
                .OrderByDescending(x => x.SubmittedAtUtc).FirstOrDefaultAsync(token)
                ?? throw new ApplicationFailure(FailureKind.Validation, "No submitted UGC content is awaiting review.");
            var correlation = Guid.NewGuid();
            if (normalized == "changes")
            {
                var message = Clean(feedback, 2000) ?? throw new ApplicationFailure(FailureKind.Validation, "Feedback is required when requesting changes.");
                assignment.RequestChanges(); submission.RequestChanges(actor.UserId, message, Now);
                Record(actor, "UgcChangesRequested", opportunity.Id, assignment.CreatorId, correlation,
                    new { OpportunityId = opportunity.Id, AssignmentId = assignment.Id, CreatorId = assignment.CreatorId, Feedback = message });
            }
            else if (normalized == "reject")
            {
                assignment.Reject(Now); submission.Reject(actor.UserId, Clean(feedback, 2000), Now);
                // Rejected accepted work remains reserved until an explicit governed cancellation policy exists.
                Record(actor, "UgcContentRejected", opportunity.Id, assignment.CreatorId, correlation,
                    new { OpportunityId = opportunity.Id, AssignmentId = assignment.Id, CreatorId = assignment.CreatorId, Feedback = Clean(feedback, 2000) });
            }
            else
            {
                var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == opportunity.BusinessId, token);
                opportunity.RecognizeApprovedDeliverable(wallet, Now, correlation);
                assignment.Approve(Now); submission.Approve(actor.UserId, Now);
                var total = assignment.CreatorPayment.Add(assignment.PlatformFee);
                var journal = new FinancialJournal(Guid.NewGuid().ToString("N"), correlation, actor.UserId, JournalSourceType.UgcApproval, Now, key);
                journal.AddLine(JournalLineType.Debit, total, "UgcAllocatedReserve");
                journal.AddLine(JournalLineType.Credit, assignment.CreatorPayment, "CreatorPayable");
                if (assignment.PlatformFee.Amount > 0) journal.AddLine(JournalLineType.Credit, assignment.PlatformFee, "PlatformRevenue");
                journal.Post(); db.FinancialJournals.Add(journal); JournalContext(journal, opportunity, assignment);
                db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, total, "UgcConsumed", journal.Id, Now, opportunity.Id));
                db.UgcBudgetEntries.Add(new(Guid.NewGuid(), opportunity.Id, assignment.Id, total, "Approved", journal.Id, Now));
                var earnings = await db.CreatorEarningsAccounts.SingleOrDefaultAsync(x => x.CreatorId == assignment.CreatorId, token);
                if (earnings is null) { earnings = new CreatorEarningsAccount(assignment.CreatorId); db.CreatorEarningsAccounts.Add(earnings); }
                var before = earnings.AvailableEarnings;
                earnings.EarnUgc(assignment.CreatorPayment, assignment.Id, Now, correlation);
                var earning = earnings.Entries.Last(); db.CreatorEarningEntries.Add(earning); db.Entry(earning).Property("JournalId").CurrentValue = journal.Id;
                if (assignment.PlatformFee.Amount > 0)
                {
                    var revenue = new PlatformRevenueEntry(Guid.NewGuid(), null, PlatformRevenueSource.UgcFee,
                        assignment.PlatformFee, RevenueStatus.Accrued, Now, correlation, assignment.Id);
                    db.PlatformRevenueEntries.Add(revenue); db.Entry(revenue).Property("JournalId").CurrentValue = journal.Id;
                }
                await operation.EmitEligibility(PayoutBeneficiary.Creator, assignment.CreatorId, before, earnings.AvailableEarnings, Now, token);
                Record(actor, "UgcContentApproved", opportunity.Id, assignment.CreatorId, correlation,
                    new { OpportunityId = opportunity.Id, AssignmentId = assignment.Id, CreatorId = assignment.CreatorId,
                        CreatorPayment = assignment.CreatorPayment.Amount, PlatformFee = assignment.PlatformFee.Amount, JournalId = journal.Id });
            }
            operation.Remember(actor, "ReviewUgcSubmission", key, fingerprint, assignment.Id.ToString(), Now);
            return assignment.Id;
        }, ct);

    public Task<Guid> UpdateAsync(Actor actor, Guid id, UgcRevisionInput input, long expectedVersion, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandBusiness(actor, token);
            var fingerprint = RequestFingerprint.Create(id.ToString(), JsonSerializer.Serialize(input), expectedVersion.ToString());
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "UpdateUgc", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var opportunity = await Opportunity(id, token); Own(actor, opportunity);
            if (opportunity.Version != expectedVersion) throw Conflict();
            var resources = Resources(input.Resources);
            var isDraft = opportunity.Status == UgcOpportunityStatus.Draft;
            var isFullEdit = isDraft || (opportunity.Status is (UgcOpportunityStatus.Open or UgcOpportunityStatus.InProgress) && opportunity.ApprovedCreatorCount == 0);
            var isMaterial = isFullEdit && !isDraft || (!isFullEdit && input.IsMaterial)
                || !string.Equals(opportunity.Instructions.Trim(), input.Instructions.Trim(), StringComparison.Ordinal)
                || !string.Equals(opportunity.Location?.Trim(), input.Location?.Trim(), StringComparison.Ordinal)
                || !string.Equals(opportunity.UsageRights?.Trim(), input.UsageRights?.Trim(), StringComparison.Ordinal);
            if (isFullEdit)
            {
                var contentType = opportunity.ContentType;
                if (input.ContentType is { } requested && (!Enum.TryParse<UgcContentType>(requested, true, out contentType) || !Enum.IsDefined(contentType)))
                    throw new ApplicationFailure(FailureKind.Validation, "Choose Video or Photos.");
                var requirements = input.PlatformRequirements is null
                    ? PostingRequirements(opportunity).Select(x => (x.Platform, x.Format, x.MinimumAudience)).ToArray()
                    : Requirements(input.PlatformRequirements);
                var capacities = input.PlatformCapacities is null
                    ? opportunity.PlatformCapacities.Where(x => x.Capacity > 0).Select(x => (x.Platform, x.Capacity)).ToArray()
                    : Capacities(input.PlatformCapacities);
                if (input.PlatformRequirements is not null && input.PlatformCapacities is null
                    && opportunity.PlatformCapacities.Count == 0
                    && (requirements.Count != opportunity.PlatformRequirements.Count
                        || requirements.Any(row => !opportunity.PlatformRequirements.Any(existing =>
                            existing.Platform == row.Platform && existing.Format == row.Format
                            && existing.MinimumAudience == row.MinimumAudience))))
                    throw new ApplicationFailure(FailureKind.Validation,
                        "Choose Creator slots when changing legacy UGC posting platforms.");
                if (input.PlatformCapacities is not null && requirements.Count > 0 && capacities.Count == 0)
                    throw new ApplicationFailure(FailureKind.Validation, "Choose Creator slots for each posting platform.");
                if (capacities.Count > 0 && (capacities.Sum(x => x.Capacity) != (input.CreatorsNeeded ?? opportunity.CreatorCapacity)
                    || requirements.Select(x => x.Platform).Except(capacities.Select(x => x.Platform)).Any()
                    || capacities.Select(x => x.Platform).Except(requirements.Select(x => x.Platform)).Any()))
                    throw new ApplicationFailure(FailureKind.Validation, "Posting platforms must match Creator slots.");
                var existingRequirementIds = opportunity.PlatformRequirements.Select(row => row.Id).ToHashSet();
                var existingCapacityIds = opportunity.PlatformCapacities.Select(row => row.Id).ToHashSet();
                var previousRequired = opportunity.RequiredFunding;
                var previousReserved = opportunity.ReservedFunding;
                opportunity.UpdateDraft(input.Title ?? opportunity.Title, input.Slogan, contentType, input.Instructions,
                    JsonSerializer.Serialize(resources), input.Location, input.DueDateUtc ?? opportunity.DueDateUtc,
                    input.ProductProvided ?? opportunity.ProductProvided, input.CreatorMustPurchase ?? opportunity.CreatorMustPurchase,
                    input.UsageRights, input.CreatorPayment is { } payment ? Amount(payment) : opportunity.CreatorPayment,
                    input.CreatorsNeeded ?? opportunity.CreatorCapacity, requirements, Now, capacities, input.ApplicationClosesAtUtc ?? opportunity.ApplicationClosesAtUtc);
                foreach (var row in opportunity.PlatformRequirements.Where(row => !existingRequirementIds.Contains(row.Id)))
                    db.UgcPlatformRequirements.Add(row);
                foreach (var row in opportunity.PlatformCapacities.Where(row => !existingCapacityIds.Contains(row.Id)))
                    db.UgcPlatformCapacities.Add(row);
                var wallet = opportunity.Status == UgcOpportunityStatus.Draft ? null : await db.BusinessWallets.SingleAsync(x => x.BusinessId == opportunity.BusinessId, token);
                if (wallet is not null)
                {
                    var target = opportunity.RequiredFunding;
                    var delta = target.Amount - previousReserved.Amount;
                    var correlation = Guid.NewGuid();
                    if (delta > 0)
                    {
                        var adjustment = new Money(delta, target.Currency); wallet.Reserve(adjustment, Now, correlation);
                        var journal = Journal(actor, JournalSourceType.UgcReservation, adjustment, "BusinessAvailable", "UgcAllocatedReserve", key, correlation, opportunity.Id, null);
                        db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, adjustment, "UgcReserveAdjustment", journal.Id, Now, opportunity.Id));
                        db.UgcBudgetEntries.Add(new(Guid.NewGuid(), opportunity.Id, null, adjustment, "Increased", journal.Id, Now));
                    }
                    else if (delta < 0)
                    {
                        var adjustment = new Money(-delta, target.Currency); wallet.ReleaseReserve(adjustment, Now, correlation);
                        var journal = Journal(actor, JournalSourceType.UgcReservation, adjustment, "UgcAllocatedReserve", "BusinessAvailable", key, correlation, opportunity.Id, null);
                        db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, adjustment, "UgcReleaseAdjustment", journal.Id, Now, opportunity.Id));
                        db.UgcBudgetEntries.Add(new(Guid.NewGuid(), opportunity.Id, null, adjustment, "Released", journal.Id, Now));
                    }
                    opportunity.SetReservedFunding(target);
                }
                else opportunity.SetReservedFunding(Money.Zero(opportunity.CreatorPayment.Currency));
                var offer = await db.UgcCustomerOffers.SingleOrDefaultAsync(x => x.UgcOpportunityId == opportunity.Id, token);
                var offerEnabled = input.CustomerOfferEnabled ?? offer is { Status: UgcCustomerOfferStatus.Active or UgcCustomerOfferStatus.Exhausted };
                if (!offerEnabled && offer is not null)
                {
                    if (wallet is not null && offer.Status == UgcCustomerOfferStatus.Active && offer.ReservedFunding.Amount > 0)
                    {
                        var released = offer.ReservedFunding; var correlation = Guid.NewGuid(); offer.Cancel(wallet, Now, correlation);
                        var journal = Journal(actor, JournalSourceType.UgcCustomerOfferReservation, released, "UgcCustomerOfferReserve", "BusinessAvailable", key, correlation, opportunity.Id, null, offer.Id);
                        db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, released, "UgcCustomerOfferReleased", journal.Id, Now, opportunity.Id, offer.Id));
                        db.UgcCustomerOfferBudgetEntries.Add(new(Guid.NewGuid(), offer.Id, null, released, "Released", journal.Id, Now));
                    }
                    else if (wallet is null && offer.Status == UgcCustomerOfferStatus.Draft) db.UgcCustomerOffers.Remove(offer);
                }
                else if (offerEnabled)
                {
                    var config = await new FinancialConfigurationResolver(db).EffectiveAsync(Now, token);
                    var ugc = config.Ugc ?? throw new ApplicationFailure(FailureKind.Validation, "UGC financial settings are unavailable.");
                    if (ugc.CustomerOfferPlatformSalePercent is not { } platformSalePercent)
                        throw new ApplicationFailure(FailureKind.Validation, "The effective financial configuration does not support UGC Customer Offers.");
                    var discount = input.CustomerDiscountPercent ?? offer?.CustomerDiscountPercent;
                    EnsureCustomerDiscount(discount);
                    if (offer is null)
                    {
                        offer = CreateCustomerOffer(opportunity, input.CustomerDiscountPercent,
                            input.CustomerOfferFundedAllocation, input.CustomerFacingSlogan,
                            input.CustomerOfferStartsAtUtc, input.CustomerOfferEndsAtUtc,
                            platformSalePercent, ugc.EffectiveFromUtc, ugc.ConfigurationVersionId);
                        db.UgcCustomerOffers.Add(offer);
                        if (wallet is not null)
                        {
                            var correlation = Guid.NewGuid();
                            offer.Publish(wallet, Now, correlation);
                            var journal = Journal(actor, JournalSourceType.UgcCustomerOfferReservation, offer.FundedLimit,
                                "BusinessAvailable", "UgcCustomerOfferReserve", key, correlation, opportunity.Id, null, offer.Id);
                            db.UgcCustomerOfferReservations.Add(new(offer.Id, opportunity.BusinessId, offer.FundedLimit, journal.Id, Now));
                            db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, offer.FundedLimit, "UgcCustomerOfferReserve", journal.Id, Now, opportunity.Id, offer.Id));
                            db.UgcCustomerOfferBudgetEntries.Add(new(Guid.NewGuid(), offer.Id, null, offer.FundedLimit, "Reserved", journal.Id, Now));
                        }
                    }
                    else
                    {
                        var previousOfferReserved = offer.ReservedFunding;
                        offer.ReviseBeforeApproval(input.CustomerFacingSlogan ?? offer.CustomerFacingSlogan,
                            input.CustomerDiscountPercent ?? offer.CustomerDiscountPercent,
                            input.CustomerOfferStartsAtUtc ?? offer.StartsAtUtc,
                            input.CustomerOfferEndsAtUtc ?? offer.EndsAtUtc,
                            input.CustomerOfferFundedAllocation is { } funded ? Amount(funded) : offer.FundedLimit);
                        if (wallet is not null && offer.Status == UgcCustomerOfferStatus.Cancelled)
                        {
                            var correlation = Guid.NewGuid(); offer.Reactivate(wallet, Now, correlation);
                            var journal = Journal(actor, JournalSourceType.UgcCustomerOfferReservation, offer.FundedLimit, "BusinessAvailable", "UgcCustomerOfferReserve", key, correlation, opportunity.Id, null, offer.Id);
                            db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, offer.FundedLimit, "UgcCustomerOfferReserve", journal.Id, Now, opportunity.Id, offer.Id));
                            db.UgcCustomerOfferBudgetEntries.Add(new(Guid.NewGuid(), offer.Id, null, offer.FundedLimit, "Reserved", journal.Id, Now));
                        }
                        else if (wallet is not null)
                        {
                            var delta = offer.FundedLimit.Amount - previousOfferReserved.Amount;
                            var correlation = Guid.NewGuid();
                            if (delta > 0)
                            {
                                var adjustment = new Money(delta, offer.FundedLimit.Currency); wallet.Reserve(adjustment, Now, correlation);
                                var journal = Journal(actor, JournalSourceType.UgcCustomerOfferReservation, adjustment, "BusinessAvailable", "UgcCustomerOfferReserve", key, correlation, opportunity.Id, null, offer.Id);
                                db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, adjustment, "UgcCustomerOfferReserveAdjustment", journal.Id, Now, opportunity.Id, offer.Id));
                                db.UgcCustomerOfferBudgetEntries.Add(new(Guid.NewGuid(), offer.Id, null, adjustment, "Increased", journal.Id, Now));
                            }
                            else if (delta < 0)
                            {
                                var adjustment = new Money(-delta, offer.FundedLimit.Currency); wallet.ReleaseReserve(adjustment, Now, correlation);
                                var journal = Journal(actor, JournalSourceType.UgcCustomerOfferReservation, adjustment, "UgcCustomerOfferReserve", "BusinessAvailable", key, correlation, opportunity.Id, null, offer.Id);
                                db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, adjustment, "UgcCustomerOfferReleaseAdjustment", journal.Id, Now, opportunity.Id, offer.Id));
                                db.UgcCustomerOfferBudgetEntries.Add(new(Guid.NewGuid(), offer.Id, null, adjustment, "Released", journal.Id, Now));
                            }
                            offer.SetReservedFunding(offer.FundedLimit);
                        }
                    }
                }
            }
            else
            {
                if (input.Title is not null || input.ContentType is not null || input.DueDateUtc is not null || input.ApplicationClosesAtUtc is not null
                    || input.ProductProvided is not null || input.CreatorMustPurchase is not null
                    || input.CreatorPayment is not null || input.CreatorsNeeded is not null || input.PlatformRequirements is not null
                    || input.PlatformCapacities is not null
                    || input.CustomerOfferEnabled is not null || input.CustomerDiscountPercent is not null
                    || input.CustomerOfferFundedAllocation is not null || input.CustomerFacingSlogan is not null
                    || input.CustomerOfferStartsAtUtc is not null || input.CustomerOfferEndsAtUtc is not null)
                    throw new ApplicationFailure(FailureKind.Validation, "Published UGC financial and deliverable terms cannot be rewritten. Create a new UGC opportunity for those changes.");
                opportunity.UpdateNonMaterial(input.Slogan, input.Instructions, JsonSerializer.Serialize(resources), input.Location, input.UsageRights);
            }
            opportunity.StartRevision();
            if (isMaterial && opportunity.Status is UgcOpportunityStatus.Open or UgcOpportunityStatus.InProgress)
                foreach (var assignment in await db.UgcAssignments.Where(x => x.UgcOpportunityId == id).ToListAsync(token)) assignment.RequireRevisionAcceptance();
            db.UgcRevisions.Add(new(opportunity.Id, opportunity.CurrentRevision, isMaterial, Snapshot(opportunity), actor.UserId, Now));
            operation.Remember(actor, "UpdateUgc", key, fingerprint, opportunity.Id.ToString(), Now);
            Record(actor, isMaterial ? "UgcMaterialRevision" : "UgcUpdated", opportunity.Id, null, Guid.NewGuid(),
                new { OpportunityId = opportunity.Id, opportunity.CurrentRevision, IsMaterial = isMaterial });
            return opportunity.Id;
        }, ct);

    public Task<Guid> AcceptRevisionAsync(Actor actor, Guid assignmentId, int revision, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandCreator(actor, token);
            var fingerprint = RequestFingerprint.Create(assignmentId.ToString(), revision.ToString());
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "AcceptUgcRevision", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var assignment = await db.UgcAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC assignment not found.");
            if (assignment.CreatorId != actor.CreatorId) throw new ApplicationFailure(FailureKind.Forbidden, "This UGC assignment belongs to another Creator.");
            var opportunity = await db.UgcOpportunities.SingleAsync(x => x.Id == assignment.UgcOpportunityId, token);
            if (revision != opportunity.CurrentRevision) throw new ApplicationFailure(FailureKind.Validation, "Review the latest UGC requirements before accepting.");
            assignment.AcceptRevision(revision);
            operation.Remember(actor, "AcceptUgcRevision", key, fingerprint, assignment.Id.ToString(), Now);
            Record(actor, "UgcRevisionAccepted", opportunity.Id, assignment.CreatorId, Guid.NewGuid(),
                new { OpportunityId = opportunity.Id, AssignmentId = assignment.Id, Revision = revision });
            return assignment.Id;
        }, ct);

    public Task<Guid> CancelAsync(Actor actor, Guid id, long expectedVersion, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandBusiness(actor, token);
            var fingerprint = RequestFingerprint.Create(id.ToString(), expectedVersion.ToString());
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "CancelUgc", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var opportunity = await Opportunity(id, token); Own(actor, opportunity);
            if (opportunity.Version != expectedVersion) throw Conflict();
            var released = opportunity.ReservedFunding;
            var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == opportunity.BusinessId, token);
            var offer = await db.UgcCustomerOffers.SingleOrDefaultAsync(x => x.UgcOpportunityId == opportunity.Id, token);
            var offerReleased = offer?.ReservedFunding ?? Money.Zero();
            var correlation = Guid.NewGuid(); opportunity.Cancel(wallet, Now, correlation); offer?.Cancel(wallet, Now, correlation);
            if (released.Amount > 0)
            {
                var journal = Journal(actor, JournalSourceType.UgcReservation, released, "UgcAllocatedReserve", "BusinessAvailable",
                    key, correlation, opportunity.Id, null);
                db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, released, "UgcReleased", journal.Id, Now, opportunity.Id));
                db.UgcBudgetEntries.Add(new(Guid.NewGuid(), opportunity.Id, null, released, "Released", journal.Id, Now));
            }
            if (offer is not null && offerReleased.Amount > 0)
            {
                var offerJournal = Journal(actor, JournalSourceType.UgcCustomerOfferReservation, offerReleased,
                    "UgcCustomerOfferReserve", "BusinessAvailable", key, correlation, opportunity.Id, null, offer.Id);
                db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, offerReleased,
                    "UgcCustomerOfferReleased", offerJournal.Id, Now, opportunity.Id, offer.Id));
                db.UgcCustomerOfferBudgetEntries.Add(new(Guid.NewGuid(), offer.Id, null, offerReleased,
                    "Released", offerJournal.Id, Now));
            }
            operation.Remember(actor, "CancelUgc", key, fingerprint, opportunity.Id.ToString(), Now);
            Record(actor, "UgcCancelled", opportunity.Id, null, correlation, new { OpportunityId = opportunity.Id, Released = released.Amount });
            return opportunity.Id;
        }, ct);

    public async Task<IReadOnlyList<UgcCard>> BusinessAsync(Actor actor, CancellationToken ct)
    {
        await DemandBusiness(actor, ct);
        var rows = await db.UgcOpportunities.AsNoTracking().Include(x => x.PlatformRequirements).Include(x => x.PlatformCapacities)
            .Where(x => x.BusinessId == actor.BusinessId).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        var offers = await db.UgcCustomerOffers.AsNoTracking().Where(x => x.BusinessId == actor.BusinessId)
            .ToDictionaryAsync(x => x.UgcOpportunityId, ct);
        var name = (await db.PublicWorkspaceProfiles.AsNoTracking().SingleAsync(x => x.Role == ActorRole.Business && x.SubjectId == actor.BusinessId, ct)).DisplayName;
        return rows.Select(x => Card(x, name, null, offers.GetValueOrDefault(x.Id), true)).ToArray();
    }

    public async Task<IReadOnlyList<UgcCard>> DiscoverAsync(Actor actor, CancellationToken ct)
    {
        await DemandCreator(actor, ct);
        var socials = await db.CreatorSocialProfiles.AsNoTracking().Where(x => x.CreatorId == actor.CreatorId && x.IsActive).ToListAsync(ct);
        var requests = await db.UgcCreatorRequests.AsNoTracking().Where(x => x.CreatorId == actor.CreatorId)
            .OrderByDescending(x => x.RequestedAtUtc).ToListAsync(ct);
        var rows = await db.UgcOpportunities.AsNoTracking().Include(x => x.PlatformRequirements).Include(x => x.PlatformCapacities)
            .Where(x => (x.Status == UgcOpportunityStatus.Open || x.Status == UgcOpportunityStatus.InProgress)
                && x.DueDateUtc > Now)
            .OrderByDescending(x => x.PublishedAtUtc).ToListAsync(ct);
        var businessIds = rows.Select(x => x.BusinessId).Distinct().ToArray();
        var activeBusinesses = await db.CommercePermissions.AsNoTracking()
            .Where(x => x.Role == ActorRole.Business && businessIds.Contains(x.SubjectId) && x.IsActive)
            .Select(x => x.SubjectId).Distinct().ToListAsync(ct);
        var names = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.Role == ActorRole.Business && businessIds.Contains(x.SubjectId))
            .ToDictionaryAsync(x => x.SubjectId, x => x.DisplayName, ct);
        var offers = await db.UgcCustomerOffers.AsNoTracking()
            .Where(x => businessIds.Contains(x.BusinessId))
            .ToDictionaryAsync(x => x.UgcOpportunityId, ct);
        var enforceAudience = (await new FinancialConfigurationResolver(db).EffectiveAsync(Now, ct)).EnforceAudienceRequirements;
        return rows.Where(x => activeBusinesses.Contains(x.BusinessId)
                && x.ReservedFunding.Amount + x.UsedFunding.Amount >= x.RequiredFunding.Amount
                && x.ProductProvided != x.CreatorMustPurchase
                && (HasPlatformSlots(x)
                    ? x.PlatformCapacities.Any(slot => slot.Available > 0 && socials.Any(profile => profile.Platform == slot.Platform && MatchesProfile(profile, x, enforceAudience)))
                    : Eligible(socials, x, enforceAudience) && x.ApprovedCreatorCount < x.CreatorCapacity))
            .Select(x => Card(x, names.GetValueOrDefault(x.BusinessId, "Business"), requests.FirstOrDefault(r => r.UgcOpportunityId == x.Id)?.Status.ToString(), offers.GetValueOrDefault(x.Id), false, socials, enforceAudience)).ToArray();
    }

    public async Task<IReadOnlyList<UgcRequestView>> CreatorRequestsAsync(Actor actor, CancellationToken ct)
    {
        await DemandCreator(actor, ct);
        var requests = await db.UgcCreatorRequests.AsNoTracking().Where(x => x.CreatorId == actor.CreatorId).OrderByDescending(x => x.RequestedAtUtc).ToListAsync(ct);
        var name = (await db.PublicWorkspaceProfiles.AsNoTracking().SingleAsync(x => x.Role == ActorRole.Creator && x.SubjectId == actor.CreatorId, ct)).DisplayName;
        var socialIds=requests.Where(x=>x.VerifiedSocialProfileId!=null).Select(x=>x.VerifiedSocialProfileId!.Value).ToArray();
        var socials=await db.CreatorSocialProfiles.AsNoTracking().Where(x=>socialIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);
        return requests.Select(x => new UgcRequestView(x.Id, x.UgcOpportunityId, x.CreatorId, name, x.Status.ToString(), x.RequestedAtUtc, x.RejectionReason,
            SocialProfile:x.VerifiedSocialProfileId is {} profileId&&socials.TryGetValue(profileId,out var profile)?SocialView(profile):null)).ToArray();
    }

    public async Task<IReadOnlyList<UgcAssignmentView>> CreatorAssignmentsAsync(Actor actor, CancellationToken ct)
    {
        await DemandCreator(actor, ct);
        var rows = await db.UgcAssignments.AsNoTracking().Where(x => x.CreatorId == actor.CreatorId).OrderByDescending(x => x.ApprovedAtUtc).ToListAsync(ct);
        return await AssignmentViews(rows, ct);
    }

    public async Task<UgcDetail> DetailAsync(Actor actor, Guid id, CancellationToken ct)
    {
        var opportunity = await db.UgcOpportunities.AsNoTracking().Include(x => x.PlatformRequirements).Include(x => x.PlatformCapacities).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC opportunity not found.");
        var isBusiness = actor.Role == ActorRole.Business && actor.BusinessId == opportunity.BusinessId;
        var isCreator = actor.Role == ActorRole.Creator && actor.CreatorId is not null;
        var isAdmin = actor.Role is ActorRole.PlatformAdmin or ActorRole.OperationsAdmin;
        if (!isBusiness && !isCreator && !isAdmin) throw new ApplicationFailure(FailureKind.Forbidden, "This UGC opportunity is not available to you.");
        if (isBusiness) await DemandBusiness(actor, ct);
        else if (isCreator)
        {
            await DemandCreator(actor, ct);
            var creatorHasContext = (opportunity.Status is UgcOpportunityStatus.Open or UgcOpportunityStatus.InProgress)
                && opportunity.DueDateUtc > Now
                || await db.UgcCreatorRequests.AsNoTracking().AnyAsync(x => x.UgcOpportunityId == id && x.CreatorId == actor.CreatorId, ct)
                || await db.UgcAssignments.AsNoTracking().AnyAsync(x => x.UgcOpportunityId == id && x.CreatorId == actor.CreatorId, ct);
            if (!creatorHasContext) throw new ApplicationFailure(FailureKind.Forbidden, "This UGC opportunity is not available to the active Creator.");
        }
        else DemandPlatformAdmin(actor);
        var business = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.Role == ActorRole.Business && x.SubjectId == opportunity.BusinessId).Select(x => x.DisplayName).SingleAsync(ct);
        var requestRows = await db.UgcCreatorRequests.AsNoTracking().Where(x => x.UgcOpportunityId == id).OrderByDescending(x => x.RequestedAtUtc).ToListAsync(ct);
        if (isCreator) requestRows = requestRows.Where(x => x.CreatorId == actor.CreatorId).ToList();
        var creatorIds = requestRows.Select(x => x.CreatorId).Distinct().ToArray();
        var creatorProfiles = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.Role == ActorRole.Creator && creatorIds.Contains(x.SubjectId)).ToDictionaryAsync(x => x.SubjectId, ct);
        var requestSocialIds=requestRows.Where(x=>x.VerifiedSocialProfileId!=null).Select(x=>x.VerifiedSocialProfileId!.Value).Distinct().ToArray();
        var requestSocials=await db.CreatorSocialProfiles.AsNoTracking().Where(x=>requestSocialIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);
        var requests = requestRows.Select(x => new UgcRequestView(x.Id, id, x.CreatorId, creatorProfiles.GetValueOrDefault(x.CreatorId)?.DisplayName ?? "Creator", x.Status.ToString(), x.RequestedAtUtc, x.RejectionReason, creatorProfiles.GetValueOrDefault(x.CreatorId)?.CreatorNumber,
            x.VerifiedSocialProfileId is {} profileId&&requestSocials.TryGetValue(profileId,out var profile)?SocialView(profile):null)).ToArray();
        var assignmentRows = await db.UgcAssignments.AsNoTracking().Where(x => x.UgcOpportunityId == id && (!isCreator || x.CreatorId == actor.CreatorId)).ToListAsync(ct);
        var assignments = await AssignmentViews(assignmentRows, ct);
        var revisions = await db.UgcRevisions.AsNoTracking().Where(x => x.UgcOpportunityId == id)
            .OrderByDescending(x => x.RevisionNumber)
            .Select(x => new UgcRevisionView(x.RevisionNumber, x.IsMaterial, x.CreatedAtUtc, x.SnapshotJson)).ToListAsync(ct);
        var status = isCreator ? requestRows.OrderByDescending(x => x.RequestedAtUtc).FirstOrDefault()?.Status.ToString() : null;
        var customerOffer = isBusiness || isAdmin
            ? await db.UgcCustomerOffers.AsNoTracking().SingleOrDefaultAsync(x => x.UgcOpportunityId == id, ct) : null;
        var socialProfiles = isCreator ? await db.CreatorSocialProfiles.AsNoTracking()
            .Where(x => x.CreatorId == actor.CreatorId && x.IsActive).ToListAsync(ct) : null;
        return new UgcDetail(Card(opportunity, business, status, customerOffer, isBusiness || isAdmin, socialProfiles), opportunity.Instructions, ParseResources(opportunity.ResourcesJson),
            opportunity.ProductProvided, opportunity.CreatorMustPurchase, opportunity.UsageRights, opportunity.CurrentRevision, requests, assignments, revisions);
    }

    public async Task<IReadOnlyList<UgcCard>> AdminAsync(Actor actor, CancellationToken ct)
    {
        DemandPlatformAdmin(actor);
        var rows = await db.UgcOpportunities.AsNoTracking().Include(x => x.PlatformRequirements).Include(x => x.PlatformCapacities).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        var ids = rows.Select(x => x.BusinessId).Distinct().ToArray();
        var names = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.Role == ActorRole.Business && ids.Contains(x.SubjectId)).ToDictionaryAsync(x => x.SubjectId, x => x.DisplayName, ct);
        var offers = await db.UgcCustomerOffers.AsNoTracking().ToDictionaryAsync(x => x.UgcOpportunityId, ct);
        return rows.Select(x => Card(x, names.GetValueOrDefault(x.BusinessId, "Business"), null, offers.GetValueOrDefault(x.Id), true)).ToArray();
    }

    public async Task<IReadOnlyList<OperationsUgcView>> OperationsAsync(Actor actor, CancellationToken ct)
    {
        if (!AdministrativeAuthority.For(new RealActor(actor)).Allows(AdministrativeCapability.UgcOperationalVisibility))
            throw new ApplicationFailure(FailureKind.Forbidden, "Operations UGC access is required.");
        var rows = await db.UgcOpportunities.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        var businessIds = rows.Select(x => x.BusinessId).Distinct().ToArray();
        var names = await db.PublicWorkspaceProfiles.AsNoTracking()
            .Where(x => x.Role == ActorRole.Business && businessIds.Contains(x.SubjectId))
            .ToDictionaryAsync(x => x.SubjectId, x => x.DisplayName, ct);
        var offers = await db.UgcCustomerOffers.AsNoTracking().ToDictionaryAsync(x => x.UgcOpportunityId, ct);
        return rows.Select(x => new OperationsUgcView(x.Id, x.BusinessId, names.GetValueOrDefault(x.BusinessId, "Business"),
            x.Title, x.Status.ToString(), x.CreatorCapacity, x.ApprovedCreatorCount, x.DueDateUtc, x.Location,
            x.CurrentRevision, offers.GetValueOrDefault(x.Id)?.Status.ToString())).ToArray();
    }

    private async Task<IReadOnlyList<UgcAssignmentView>> AssignmentViews(IReadOnlyCollection<UgcAssignment> rows, CancellationToken ct)
    {
        var opportunityIds = rows.Select(x => x.UgcOpportunityId).Distinct().ToArray();
        var opportunities = await db.UgcOpportunities.AsNoTracking().Include(x => x.PlatformRequirements).Include(x => x.PlatformCapacities)
            .Where(x => opportunityIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var businessIds = opportunities.Values.Select(x => x.BusinessId).Distinct().ToArray();
        var creatorIds = rows.Select(x => x.CreatorId).Distinct().ToArray();
        var profiles = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => businessIds.Contains(x.SubjectId) || creatorIds.Contains(x.SubjectId)).ToListAsync(ct);
        var submissions = await db.UgcSubmissions.AsNoTracking().Where(x => rows.Select(r => r.Id).Contains(x.UgcAssignmentId)).OrderByDescending(x => x.SubmittedAtUtc).ToListAsync(ct);
        var assetIds = submissions.Where(x => x.ReviewMediaAssetId != null).Select(x => x.ReviewMediaAssetId!.Value).Distinct().ToArray();
        var assets = await db.PrivateReviewMediaAssets.AsNoTracking().Where(x => assetIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var assignmentIds = rows.Select(x => x.Id).ToArray();
        var publications = await db.CreatorPublicationVerifications.AsNoTracking()
            .Where(x => x.UgcAssignmentId != null && assignmentIds.Contains(x.UgcAssignmentId.Value))
            .OrderByDescending(x => x.RequestedAtUtc).ToListAsync(ct);
        var requestIds = rows.Select(x => x.UgcCreatorRequestId).ToArray();
        var requests = await db.UgcCreatorRequests.AsNoTracking().Where(x => requestIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var socialIds = requests.Values.Where(x => x.VerifiedSocialProfileId != null)
            .Select(x => x.VerifiedSocialProfileId!.Value).Distinct().ToArray();
        var socials = await db.CreatorSocialProfiles.AsNoTracking().Where(x => socialIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        return rows.Select(x =>
        {
            var opportunity = opportunities[x.UgcOpportunityId]; var submission = submissions.FirstOrDefault(s => s.UgcAssignmentId == x.Id);
            var publication = publications.FirstOrDefault(p => p.UgcAssignmentId == x.Id);
            var request = requests[x.UgcCreatorRequestId];
            var social = request.VerifiedSocialProfileId is { } socialId ? socials.GetValueOrDefault(socialId) : null;
            return new UgcAssignmentView(x.Id, x.UgcOpportunityId, opportunity.Title, opportunity.BusinessId,
                profiles.FirstOrDefault(p => p.SubjectId == opportunity.BusinessId && p.Role == ActorRole.Business)?.DisplayName ?? "Business",
                x.CreatorId, profiles.FirstOrDefault(p => p.SubjectId == x.CreatorId && p.Role == ActorRole.Creator)?.DisplayName ?? "Creator",
                x.CreatorPayment.Amount, x.Status.ToString(), x.AcceptedRevisionNumber, x.RevisionAcceptanceRequired,
                x.ContentDueAtUtc ?? opportunity.DueDateUtc, opportunity.Instructions, ParseResources(opportunity.ResourcesJson), opportunity.Location,
                PostingRequirements(opportunity).Select(p => new UgcPlatformRequirementView(p.Platform.ToString(), p.Format, p.MinimumAudience)).ToArray(),
                submission?.Feedback, submission?.SubmissionUrl, opportunity.ProductProvided, opportunity.CreatorMustPurchase,
                profiles.FirstOrDefault(p => p.SubjectId == x.CreatorId && p.Role == ActorRole.Creator)?.CreatorNumber,
                submission?.ContentRevisionNumber,
                submission?.ReviewMediaAssetId is null ? null : $"/api/review-media/ugc/{submission.Id}",
                publication is null ? null : CreatorPublicationService.View(publication),
                request.SelectedPlatform?.ToString(), social?.ProfileUrl, social?.Id,
                submission?.ReviewMediaAssetId is { } assetId ? assets.GetValueOrDefault(assetId)?.ContentType : null);
        }).ToArray();
    }

    private async Task<UgcOpportunity> Opportunity(Guid id, CancellationToken ct) =>
        await db.UgcOpportunities.Include(x => x.PlatformRequirements).Include(x => x.PlatformCapacities).SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC opportunity not found.");
    private async Task DemandBusiness(Actor actor, CancellationToken ct)
    {
        if (actor.Role != ActorRole.Business || actor.BusinessId is null ||
            !await db.CommercePermissions.AnyAsync(x => x.UserId == actor.UserId && x.Role == ActorRole.Business && x.SubjectId == actor.BusinessId && x.IsActive, ct))
            throw new ApplicationFailure(FailureKind.Forbidden, "This Business workspace is not available to you.");
    }
    private async Task DemandCreator(Actor actor, CancellationToken ct)
    {
        if (actor.Role != ActorRole.Creator || actor.CreatorId is null ||
            !await db.CommercePermissions.AnyAsync(x => x.UserId == actor.UserId && x.Role == ActorRole.Creator && x.SubjectId == actor.CreatorId && x.IsActive, ct))
            throw new ApplicationFailure(FailureKind.Forbidden, "This Creator workspace is not available to you.");
    }
    private static void DemandPlatformAdmin(Actor actor)
    { if (!AdministrativeAuthority.For(new RealActor(actor)).Allows(AdministrativeCapability.PlatformFinancialReports)) throw new ApplicationFailure(FailureKind.Forbidden, "Platform Admin access is required."); }
    private static void Own(Actor actor, UgcOpportunity opportunity)
    { if (actor.BusinessId != opportunity.BusinessId) throw new ApplicationFailure(FailureKind.Forbidden, "This UGC opportunity belongs to another Business."); }

    private async Task EnsureCreatorEligibility(Guid creatorId, UgcOpportunity opportunity, bool enforceAudience, CancellationToken ct)
    {
        var socials = await db.CreatorSocialProfiles.AsNoTracking().Where(x => x.CreatorId == creatorId && x.IsActive).ToListAsync(ct);
        if (!Eligible(socials, opportunity, enforceAudience)) throw new ApplicationFailure(FailureKind.Validation, "Your Creator social profiles do not meet this UGC opportunity's optional platform requirement.");
    }
    private async Task EnsureEligibleProfile(Guid creatorId, CreatorPlatform platform, Guid profileId,
        UgcOpportunity opportunity, bool enforceAudience, CancellationToken ct)
    {
        var profile = await db.CreatorSocialProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == profileId, ct);
        if (profile is null || profile.CreatorId != creatorId || profile.Platform != platform || !profile.IsActive
            || !MatchesProfile(profile, opportunity, enforceAudience))
            throw new ApplicationFailure(FailureKind.Validation, "Choose an eligible social profile for this UGC platform.");
    }
    private static bool HasPlatformSlots(UgcOpportunity opportunity) =>
        opportunity.PlatformCapacities.Any(slot => slot.Capacity > 0);
    private static IReadOnlyList<UgcPlatformRequirement> PostingRequirements(UgcOpportunity opportunity) =>
        opportunity.PlatformCapacities.Count == 0
            ? opportunity.PlatformRequirements
            : opportunity.PlatformRequirements.Where(requirement => opportunity.PlatformCapacities.Any(
                slot => slot.Platform == requirement.Platform && slot.Capacity > 0)).ToArray();
    private static bool MatchesProfile(CreatorSocialProfileRecord profile, UgcOpportunity opportunity, bool enforceAudience)
    {
        var requirement = PostingRequirements(opportunity).SingleOrDefault(x => x.Platform == profile.Platform);
        return requirement is not null && SocialAudienceEligibility.Matches(profile, requirement, enforceAudience);
    }
    private static bool Eligible(IReadOnlyCollection<CreatorSocialProfileRecord> socials, UgcOpportunity opportunity, bool enforceAudience) =>
        HasPlatformSlots(opportunity)
            ? opportunity.PlatformCapacities.Any(slot => slot.Available > 0 && socials.Any(profile =>
                profile.Platform == slot.Platform && MatchesProfile(profile, opportunity, enforceAudience)))
            : PostingRequirements(opportunity).All(requirement =>
                socials.Any(s => MatchesProfile(s, opportunity, enforceAudience)));

    private FinancialJournal Journal(Actor actor, JournalSourceType source, Money amount, string debit, string credit,
        string key, Guid correlation, Guid opportunityId, Guid? assignmentId, Guid? customerOfferId = null)
    {
        var journal = new FinancialJournal(Guid.NewGuid().ToString("N"), correlation, actor.UserId, source, Now, key);
        journal.AddLine(JournalLineType.Debit, amount, debit); journal.AddLine(JournalLineType.Credit, amount, credit); journal.Post();
        db.FinancialJournals.Add(journal);
        db.Entry(journal).Property("BusinessId").CurrentValue = actor.BusinessId;
        db.Entry(journal).Property("UgcOpportunityId").CurrentValue = opportunityId;
        db.Entry(journal).Property("UgcAssignmentId").CurrentValue = assignmentId;
        db.Entry(journal).Property("UgcCustomerOfferId").CurrentValue = customerOfferId;
        return journal;
    }
    private void JournalContext(FinancialJournal journal, UgcOpportunity opportunity, UgcAssignment assignment)
    {
        db.Entry(journal).Property("BusinessId").CurrentValue = opportunity.BusinessId;
        db.Entry(journal).Property("CreatorId").CurrentValue = assignment.CreatorId;
        db.Entry(journal).Property("UgcOpportunityId").CurrentValue = opportunity.Id;
        db.Entry(journal).Property("UgcAssignmentId").CurrentValue = assignment.Id;
    }
    private void Record(Actor actor, string type, Guid opportunityId, Guid? creatorId, Guid correlation, object payload)
    {
        db.AuditEvents.Add(new(Guid.NewGuid(), type, actor.UserId, actor.BusinessId, null, creatorId, correlation, Now, "", opportunityId));
        db.OutboxMessages.Add(new() { EventType = type, Payload = JsonSerializer.Serialize(payload), OccurredAtUtc = Now });
    }
    private static Money Amount(decimal value)
    { if (value <= 0 || value > 9999999999999999.99m || decimal.Round(value, 2) != value) throw new ApplicationFailure(FailureKind.Validation, "Enter a positive amount with no more than two decimal places."); return new(value); }
    private static string ExternalUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1000 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) throw new ApplicationFailure(FailureKind.Validation, "Enter a secure HTTPS submission link.");
        return uri.AbsoluteUri;
    }
    private static bool MatchesRequiredSocialPlatform(string url, IReadOnlyCollection<UgcPlatformRequirement> requirements)
    {
        var host = new Uri(url).Host.ToLowerInvariant();
        return requirements.Any(x => x.Platform switch
        {
            CreatorPlatform.TikTok => host == "tiktok.com" || host.EndsWith(".tiktok.com", StringComparison.Ordinal),
            CreatorPlatform.Instagram => host == "instagram.com" || host.EndsWith(".instagram.com", StringComparison.Ordinal),
            CreatorPlatform.YouTube => host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal) || host == "youtu.be",
            CreatorPlatform.Facebook => host == "facebook.com" || host.EndsWith(".facebook.com", StringComparison.Ordinal) || host == "fb.watch",
            _ => false
        });
    }
    private static string[] Resources(IReadOnlyList<string>? resources) => (resources ?? [])
        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(ExternalUrl).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
    private static IReadOnlyList<(CreatorPlatform Platform, string Format, long? MinimumAudience)> Requirements(IReadOnlyList<UgcPlatformRequirementInput>? input)
    {
        var result = new List<(CreatorPlatform, string, long?)>();
        foreach (var row in input ?? [])
        {
            if (!Enum.TryParse<CreatorPlatform>(row.Platform, true, out var platform) || !Enum.IsDefined(platform))
                throw new ApplicationFailure(FailureKind.Validation, "Choose a supported social platform.");
            if (row.MinimumAudience is < 0)
                throw new ApplicationFailure(FailureKind.Validation, "Audience minimum cannot be negative.");
            result.Add((platform, row.Format, row.MinimumAudience is 0 ? null : row.MinimumAudience));
        }
        return result;
    }
    private static IReadOnlyList<(CreatorPlatform Platform, int Capacity)> Capacities(IReadOnlyList<UgcPlatformCapacityInput>? input)
    {
        var result = new List<(CreatorPlatform, int)>();
        foreach (var row in input ?? [])
        {
            if (!Enum.TryParse<CreatorPlatform>(row.Platform, true, out var platform) || !Enum.IsDefined(platform)
                || row.Capacity is < 1 or > 100 || row.MinimumAudience is < 0)
                throw new ApplicationFailure(FailureKind.Validation, "Choose supported UGC platforms and valid Creator capacity.");
            result.Add((platform, row.Capacity));
        }
        if (result.GroupBy(x => x.Item1).Any(x => x.Count() > 1))
            throw new ApplicationFailure(FailureKind.Validation, "Add each UGC platform once.");
        return result;
    }
    private static CreatorSocialProfileView SocialView(CreatorSocialProfileRecord profile) =>
        new(profile.Id, profile.Platform.ToString(), profile.ProfileUrl, profile.SelfReportedAudience,
            profile.VerificationStatus, profile.VerifiedAudience, profile.AudienceVerificationSource);
    private static UgcCard Card(UgcOpportunity x, string business, string? requestStatus, UgcCustomerOffer? offer,
        bool includeBusinessFinancials, IReadOnlyCollection<CreatorSocialProfileRecord>? socials = null, bool enforceAudience = false) => new(x.Id, x.BusinessId, business,
        x.Title, x.Slogan, x.ContentType.ToString(), x.Status.ToString(),
        includeBusinessFinancials ? x.RequiredFunding.Amount / x.CreatorCapacity : x.CreatorPayment.Amount, x.CreatorCapacity,
        x.ApprovedCreatorCount, includeBusinessFinancials ? x.RequiredFunding.Amount : null,
        includeBusinessFinancials ? x.ReservedFunding.Amount : null, includeBusinessFinancials ? x.UsedFunding.Amount : null, x.DueDateUtc,
        x.Location, PostingRequirements(x).Select(p => new UgcPlatformRequirementView(p.Platform.ToString(), p.Format, p.MinimumAudience)).ToArray(), requestStatus, x.Version,
        offer is { Status: UgcCustomerOfferStatus.Active or UgcCustomerOfferStatus.Exhausted },
        offer?.CustomerDiscountPercent,
        includeBusinessFinancials ? offer?.FundedLimit.Amount : null,
        includeBusinessFinancials ? offer?.RemainingFunding.Amount : null,
        includeBusinessFinancials ? offer?.Status.ToString() : null,
        includeBusinessFinancials ? offer?.CustomerFacingSlogan : null,
        includeBusinessFinancials ? x.PricingSnapshot.PlatformFeePercent : null,
        includeBusinessFinancials ? x.PlatformFee.Amount : null, x.ProductProvided, x.CreatorMustPurchase,
        x.PlatformCapacities.Where(slot => slot.Capacity > 0).Select(slot => new UgcPlatformCapacityView(slot.Platform.ToString(), slot.Capacity, slot.ApprovedCount, slot.Available)).ToArray(),
        socials?.Where(profile => profile.IsActive && x.PlatformCapacities.Any(slot => slot.Platform == profile.Platform && slot.Capacity > 0)
            && MatchesProfile(profile, x, enforceAudience)).Select(profile => new UgcEligibleSocialProfile(profile.Id, profile.Platform.ToString(), profile.ProfileUrl)).ToArray(),
        x.ApplicationClosesAtUtc);
    private UgcCustomerOffer CreateCustomerOffer(UgcOpportunity opportunity, decimal? discount,
        decimal? fundedAllocation, string? slogan, DateTime? starts, DateTime? ends,
        decimal platformSalePercent, DateTime effectiveFrom, Guid configurationVersionId)
    {
        if (discount is null || fundedAllocation is null || starts is null || ends is null)
            throw new ApplicationFailure(FailureKind.Validation, "Customer cashback, funded allocation, start, and end are required when Customer Offer is ON.");
        EnsureCustomerDiscount(discount);
        try
        {
            return new UgcCustomerOffer(opportunity.Id, opportunity.BusinessId, slogan, discount.Value,
                starts.Value, ends.Value, Amount(fundedAllocation.Value),
                new(platformSalePercent, effectiveFrom, configurationVersionId), Now);
        }
        catch (ArgumentException ex) { throw new ApplicationFailure(FailureKind.Validation, ex.Message, ex); }
    }
    private static void EnsureCustomerDiscount(decimal? discount)
    {
        if (discount is null || discount <= 0 || discount > 100 || decimal.Round(discount.Value, 4) != discount.Value)
            throw new ApplicationFailure(FailureKind.Validation, "Customer benefit must be a valid percentage between 0 and 100.");
    }
    private static string[] ParseResources(string json)
    { try { return JsonSerializer.Deserialize<string[]>(json) ?? []; } catch (JsonException) { return []; } }
    private static string Snapshot(UgcOpportunity x) => JsonSerializer.Serialize(new
    { x.Title, x.Slogan, x.ContentType, x.Instructions, x.ResourcesJson, x.Location, x.DueDateUtc, x.ProductProvided, x.CreatorMustPurchase, x.UsageRights, x.CreatorPayment, x.CreatorCapacity,
        PlatformCapacities = x.PlatformCapacities.Where(slot => slot.Capacity > 0).Select(slot => new { slot.Platform, slot.Capacity, slot.ApprovedCount }).ToArray(), x.CurrentRevision });
    private static string? Clean(string? value, int max)
    { if (string.IsNullOrWhiteSpace(value)) return null; var result = value.Trim(); if (result.Length > max) throw new ApplicationFailure(FailureKind.Validation, "The supplied information is too long."); return result; }
    private static ApplicationFailure Conflict() => new(FailureKind.ConcurrencyConflict, "UGC changed. Reload before trying again.");
}
